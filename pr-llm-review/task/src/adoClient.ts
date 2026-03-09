import * as tl from 'azure-pipelines-task-lib/task';
import * as azdev from 'azure-devops-node-api';
import { IGitApi } from 'azure-devops-node-api/GitApi';
import { GitVersionType } from 'azure-devops-node-api/interfaces/GitInterfaces';
import { PrInfo, ChangedFile } from './models';

export class AdoClient {
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

  async getPrInfo(): Promise<PrInfo> {
    const collectionUrl = tl.getVariable('System.TeamFoundationCollectionUri') ?? '';
    const projectId = tl.getVariable('System.TeamProjectId') ?? '';
    const repositoryId = tl.getVariable('Build.Repository.ID') ?? '';
    const prIdStr = tl.getVariable('System.PullRequest.PullRequestId') ?? '';
    const prId = parseInt(prIdStr, 10);

    if (isNaN(prId)) {
      throw new Error('This task must run in a PR pipeline. System.PullRequest.PullRequestId is not set.');
    }

    const git = await this.getGitApi();
    const pr = await git.getPullRequest(repositoryId, prId, projectId);

    const iterations = await git.getPullRequestIterations(repositoryId, prId, projectId);
    if (!iterations || iterations.length === 0) {
      throw new Error(`No iterations found for PR ${prId}`);
    }

    const latest = iterations[iterations.length - 1];
    const iterationId = latest.id ?? 1;
    const headCommitSha = latest.sourceRefCommit?.commitId ?? '';
    const baseCommitSha = latest.commonRefCommit?.commitId ?? latest.targetRefCommit?.commitId ?? '';

    return {
      prId,
      title: pr.title ?? '',
      description: pr.description ?? '',
      sourceRefName: pr.sourceRefName ?? '',
      targetRefName: pr.targetRefName ?? '',
      repositoryId,
      projectId,
      collectionUrl,
      iterationId,
      headCommitSha,
      baseCommitSha,
    };
  }

  async getChangedFiles(prInfo: PrInfo): Promise<ChangedFile[]> {
    const git = await this.getGitApi();
    const changes = await git.getPullRequestIterationChanges(
      prInfo.repositoryId,
      prInfo.prId,
      prInfo.iterationId,
      prInfo.projectId
    );

    const entries = changes.changeEntries ?? [];
    return entries
      .filter(e => e.item?.path)
      .map(e => {
        const changeType = mapChangeType(e.changeType);
        return { path: e.item!.path!, changeType };
      });
  }

  async getFileContent(prInfo: PrInfo, filePath: string, commitSha: string): Promise<string> {
    if (!commitSha) return '';
    const git = await this.getGitApi();
    try {
      const stream = await git.getItemContent(
        prInfo.repositoryId,
        filePath,
        prInfo.projectId,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        { versionType: GitVersionType.Commit, version: commitSha }
      );
      return streamToString(stream);
    } catch {
      return '';
    }
  }
}

function mapChangeType(ct: number | undefined): ChangedFile['changeType'] {
  switch (ct) {
    case 1:  return 'add';
    case 2:  return 'edit';
    case 4:  return 'delete';
    case 8:  return 'rename';
    default: return 'unknown';
  }
}

async function streamToString(stream: NodeJS.ReadableStream): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    stream.on('data', chunk => chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)));
    stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    stream.on('error', reject);
  });
}
