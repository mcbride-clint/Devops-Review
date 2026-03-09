import * as tl from 'azure-pipelines-task-lib/task';
import * as azdev from 'azure-devops-node-api';
import { IGitApi } from 'azure-devops-node-api/GitApi';
import { CommentThreadStatus } from 'azure-devops-node-api/interfaces/GitInterfaces';
import { InlineComment, LlmReviewResult, PrInfo, Severity } from './models';

const SEVERITY_BADGE: Record<Severity, string> = {
  critical: '🔴 Critical',
  high:     '🟠 High',
  medium:   '🟡 Medium',
  low:      '🟢 Low',
  info:     'ℹ️ Info',
};

export class CommentPoster {
  private gitApi: IGitApi | null = null;

  private async getGitApi(): Promise<IGitApi> {
    if (this.gitApi) return this.gitApi;
    const collectionUrl = tl.getVariable('System.TeamFoundationCollectionUri') ?? '';
    const token = tl.getVariable('SYSTEM_ACCESSTOKEN') ?? '';
    const authHandler = azdev.getPersonalAccessTokenHandler(token);
    const connection = new azdev.WebApi(collectionUrl, authHandler);
    this.gitApi = await connection.getGitApi();
    return this.gitApi;
  }

  async postSummary(
    prInfo: PrInfo,
    result: LlmReviewResult,
    fileCount: number,
    skippedFiles: string[],
    modelName: string
  ): Promise<void> {
    const git = await this.getGitApi();
    const content = buildSummaryComment(result, fileCount, skippedFiles, modelName);

    await git.createThread(
      {
        comments: [{ parentCommentId: 0, content, commentType: 1 }],
        status: CommentThreadStatus.Active,
      },
      prInfo.repositoryId,
      prInfo.prId,
      prInfo.projectId
    );
  }

  async postInlineComments(prInfo: PrInfo, comments: InlineComment[]): Promise<void> {
    const git = await this.getGitApi();

    for (const comment of comments) {
      const content = buildInlineCommentContent(comment);

      try {
        await git.createThread(
          {
            comments: [{ parentCommentId: 0, content, commentType: 1 }],
            status: CommentThreadStatus.Active,
            threadContext: {
              filePath: comment.filePath,
              rightFileStart: { line: comment.line, offset: 1 },
              rightFileEnd:   { line: comment.line, offset: 1 },
            },
          },
          prInfo.repositoryId,
          prInfo.prId,
          prInfo.projectId
        );
      } catch (err) {
        // Log but continue — a bad line number shouldn't abort all comments
        tl.warning(`Failed to post inline comment on ${comment.filePath}:${comment.line} — ${String(err)}`);
      }
    }
  }
}

function buildSummaryComment(
  result: LlmReviewResult,
  fileCount: number,
  skippedFiles: string[],
  modelName: string
): string {
  const timestamp = new Date().toISOString().replace('T', ' ').split('.')[0] + ' UTC';
  const counts = countBySeverity(result.inlineComments);

  const skippedSection = skippedFiles.length > 0
    ? `\n\n> **Note:** ${skippedFiles.length} file(s) exceeded the review limit and were skipped:\n${skippedFiles.map(f => `> - \`${f}\``).join('\n')}`
    : '';

  return `## 🤖 LLM Code Review

> Reviewed by ${modelName} • ${fileCount} files • ${timestamp}

### Overall Assessment

${result.summary}

### Findings by Severity

| Severity | Count |
|----------|-------|
| 🔴 Critical | ${counts.critical} |
| 🟠 High     | ${counts.high} |
| 🟡 Medium   | ${counts.medium} |
| 🟢 Low      | ${counts.low} |
| ℹ️ Info     | ${counts.info} |

Inline comments have been added to the changed lines above.${skippedSection}`;
}

function buildInlineCommentContent(comment: InlineComment): string {
  const badge = SEVERITY_BADGE[comment.severity] ?? comment.severity;
  const category = capitalise(comment.category);
  return `**[${badge} • ${category}]** ${comment.comment}`;
}

function countBySeverity(comments: InlineComment[]): Record<Severity, number> {
  const counts: Record<Severity, number> = { critical: 0, high: 0, medium: 0, low: 0, info: 0 };
  for (const c of comments) {
    if (c.severity in counts) counts[c.severity]++;
  }
  return counts;
}

function capitalise(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}
