export interface TaskInputs {
  llmBaseUrl: string;
  llmModel: string;
  llmApiKey: string;
  caBundlePath: string;
  maxFilesPerReview: number;
  maxLinesPerFile: number;
  excludePatterns: string[];
  postInlineComments: boolean;
  failOnSeverity: string;
}

export interface PrInfo {
  prId: number;
  title: string;
  description: string;
  sourceRefName: string;
  targetRefName: string;
  repositoryId: string;
  projectId: string;
  collectionUrl: string;
  iterationId: number;
  headCommitSha: string;
  baseCommitSha: string;
}

export interface ChangedFile {
  path: string;
  changeType: 'add' | 'edit' | 'delete' | 'rename' | 'unknown';
}

export interface DiffChunk {
  filePath: string;
  diffContent: string;
  lineCount: number;
  truncated: boolean;
}

export type Severity = 'critical' | 'high' | 'medium' | 'low' | 'info';
export type OverallSeverity = 'critical' | 'high' | 'medium' | 'low' | 'pass';
export type Category = 'security' | 'correctness' | 'sql' | 'quality' | 'style';

export interface InlineComment {
  filePath: string;
  line: number;
  severity: Severity;
  category: Category;
  comment: string;
}

export interface LlmReviewResult {
  summary: string;
  overallSeverity: OverallSeverity;
  inlineComments: InlineComment[];
}
