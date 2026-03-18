import * as tl from 'azure-pipelines-task-lib/task';
import { TaskInputs, LlmReviewResult, OverallSeverity } from './models';
import { AdoClient } from './adoClient';
import { DiffParser } from './diffParser';
import { FileFilter } from './fileFilter';
import { LlmClient, mergeResults } from './llmClient';
import { CommentPoster } from './commentPoster';
import { buildSystemPrompt, buildUserPrompt } from './promptBuilder';
import { applyCaBundle } from './caConfig';

const SEVERITY_ORDER: OverallSeverity[] = ['pass', 'low', 'medium', 'high', 'critical'];

async function run(): Promise<void> {
  try {
    const inputs = readInputs();

    // Apply custom CA bundle before any outbound HTTPS connections are made
    if (inputs.caBundlePath) {
      tl.debug(`Applying custom CA bundle from: ${inputs.caBundlePath}`);
      applyCaBundle(inputs.caBundlePath);
    }

    const adoClient   = new AdoClient();
    const diffParser  = new DiffParser(adoClient);
    const fileFilter  = new FileFilter(inputs.excludePatterns);
    const llmClient   = new LlmClient(inputs);
    const poster      = new CommentPoster();

    // 1. Fetch PR info
    tl.debug('Fetching PR info...');
    const prInfo = await adoClient.getPrInfo();
    console.log(`Reviewing PR #${prInfo.prId}: ${prInfo.title}`);

    // 2. Fetch changed files
    tl.debug('Fetching changed files...');
    const allFiles = await adoClient.getChangedFiles(prInfo);
    console.log(`Found ${allFiles.length} changed file(s)`);

    // 3. Filter files
    const { included, skipped, excluded } = fileFilter.filter(allFiles, inputs.maxFilesPerReview);
    console.log(`Included: ${included.length}, Skipped (cap): ${skipped.length}, Excluded (pattern): ${excluded.length}`);

    if (included.length === 0) {
      const msg = 'No reviewable files found after filtering. Nothing to review.';
      console.log(msg);
      tl.setResult(tl.TaskResult.Succeeded, msg);
      return;
    }

    // 4. Build diff chunks
    tl.debug('Building diffs...');
    const diffChunks = await diffParser.buildDiffChunks(prInfo, included, inputs.maxLinesPerFile);
    const llmBatches = diffParser.splitIntoLlmChunks(diffChunks);
    console.log(`Sending ${diffChunks.length} file diff(s) in ${llmBatches.length} LLM chunk(s)`);

    // 5. Send each batch to LLM
    const batchResults: LlmReviewResult[] = [];
    for (let i = 0; i < llmBatches.length; i++) {
      console.log(`Calling LLM — chunk ${i + 1}/${llmBatches.length}...`);
      try {
        const systemPrompt = buildSystemPrompt(inputs.focusAreas);
        const userPrompt = buildUserPrompt(prInfo, llmBatches[i]);
        const result = await llmClient.review(userPrompt, systemPrompt);
        batchResults.push(result);
      } catch (err) {
        tl.warning(`LLM call for chunk ${i + 1} failed: ${String(err)}`);
        batchResults.push({
          summary: `LLM review unavailable for chunk ${i + 1}: ${String(err)}`,
          overallSeverity: 'pass',
          inlineComments: [],
        });
      }
    }

    // 6. Merge results
    const merged = mergeResults(batchResults);
    const skippedPaths = skipped.map(f => f.path);

    // 7. Post summary comment
    console.log('Posting summary comment...');
    await poster.postSummary(prInfo, merged, included.length, skippedPaths, inputs.llmModel);

    // 8. Post inline comments
    if (inputs.postInlineComments && merged.inlineComments.length > 0) {
      console.log(`Posting ${merged.inlineComments.length} inline comment(s)...`);
      await poster.postInlineComments(prInfo, merged.inlineComments);
    }

    // 9. Evaluate failOnSeverity
    const shouldFail = shouldFailPipeline(inputs.failOnSeverity, merged.overallSeverity);
    if (shouldFail) {
      tl.setResult(
        tl.TaskResult.Failed,
        `LLM review found ${merged.overallSeverity} severity issues — pipeline failed as per failOnSeverity setting.`
      );
    } else {
      tl.setResult(tl.TaskResult.Succeeded, 'LLM code review complete.');
    }
  } catch (err: unknown) {
    tl.setResult(tl.TaskResult.Failed, `Task failed: ${String(err)}`);
  }
}

function readInputs(): TaskInputs {
  const llmBaseUrl   = tl.getInput('llmBaseUrl', true) ?? '';
  const llmModel     = tl.getInput('llmModel', true) ?? '';
  const llmApiKey    = tl.getInput('llmApiKey', false) ?? 'none';
  const caBundlePath = tl.getInput('caBundlePath', false) ?? '';

  const maxFilesPerReview = parseInt(tl.getInput('maxFilesPerReview', false) ?? '20', 10);
  const maxLinesPerFile   = parseInt(tl.getInput('maxLinesPerFile', false) ?? '300', 10);

  const excludePatternsRaw = tl.getInput('excludePatterns', false) ?? '';
  const excludePatterns = excludePatternsRaw
    .split(',')
    .map(p => p.trim())
    .filter(p => p.length > 0);

  const postInlineComments = (tl.getInput('postInlineComments', false) ?? 'true').toLowerCase() === 'true';
  const failOnSeverity = (tl.getInput('failOnSeverity', false) ?? 'none').toLowerCase();

  const focusAreasRaw = tl.getInput('focusAreas', false) ?? '';
  const focusAreas = focusAreasRaw
    .split(',')
    .map(a => a.trim())
    .filter(a => a.length > 0);

  return { llmBaseUrl, llmModel, llmApiKey, caBundlePath, maxFilesPerReview, maxLinesPerFile, excludePatterns, postInlineComments, failOnSeverity, focusAreas };
}

function shouldFailPipeline(failOnSeverity: string, actual: OverallSeverity): boolean {
  if (failOnSeverity === 'none') return false;
  const threshold = SEVERITY_ORDER.indexOf(failOnSeverity as OverallSeverity);
  const actualIndex = SEVERITY_ORDER.indexOf(actual);
  if (threshold < 0 || actualIndex < 0) return false;
  return actualIndex >= threshold;
}

run();
