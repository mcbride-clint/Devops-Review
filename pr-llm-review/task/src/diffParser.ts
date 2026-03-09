import * as Diff from 'diff';
import { AdoClient } from './adoClient';
import { ChangedFile, DiffChunk, PrInfo } from './models';

const CHUNK_SIZE_LINES = 3000;

export class DiffParser {
  constructor(private readonly adoClient: AdoClient) {}

  async buildDiffChunks(
    prInfo: PrInfo,
    files: ChangedFile[],
    maxLinesPerFile: number
  ): Promise<DiffChunk[]> {
    const chunks: DiffChunk[] = [];

    for (const file of files) {
      const chunk = await this.buildFileDiff(prInfo, file, maxLinesPerFile);
      if (chunk) {
        chunks.push(chunk);
      }
    }

    return chunks;
  }

  private async buildFileDiff(
    prInfo: PrInfo,
    file: ChangedFile,
    maxLinesPerFile: number
  ): Promise<DiffChunk | null> {
    let baseContent = '';
    let headContent = '';

    if (file.changeType !== 'add') {
      baseContent = await this.adoClient.getFileContent(prInfo, file.path, prInfo.baseCommitSha);
    }

    if (file.changeType !== 'delete') {
      headContent = await this.adoClient.getFileContent(prInfo, file.path, prInfo.headCommitSha);
    }

    const patch = Diff.createPatch(file.path, baseContent, headContent, 'base', 'head');

    // Remove the first two header lines (--- base / +++ head) added by createPatch
    const lines = patch.split('\n');
    const diffLines = lines.slice(4); // skip file header lines

    let truncated = false;
    let finalLines = diffLines;

    if (diffLines.length > maxLinesPerFile) {
      finalLines = diffLines.slice(0, maxLinesPerFile);
      finalLines.push(`\n[... truncated: file exceeds ${maxLinesPerFile} lines. Remaining ${diffLines.length - maxLinesPerFile} lines skipped ...]`);
      truncated = true;
    }

    const diffContent = finalLines.join('\n').trim();
    if (!diffContent) return null;

    return {
      filePath: file.path,
      diffContent,
      lineCount: finalLines.length,
      truncated,
    };
  }

  splitIntoLlmChunks(chunks: DiffChunk[]): DiffChunk[][] {
    const batches: DiffChunk[][] = [];
    let currentBatch: DiffChunk[] = [];
    let currentLines = 0;

    for (const chunk of chunks) {
      if (currentLines + chunk.lineCount > CHUNK_SIZE_LINES && currentBatch.length > 0) {
        batches.push(currentBatch);
        currentBatch = [];
        currentLines = 0;
      }
      currentBatch.push(chunk);
      currentLines += chunk.lineCount;
    }

    if (currentBatch.length > 0) {
      batches.push(currentBatch);
    }

    return batches;
  }
}
