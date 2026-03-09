import { DiffChunk, PrInfo } from './models';

export const SYSTEM_PROMPT = `You are an expert .NET and Oracle code reviewer embedded in a CI/CD pipeline.
Your job is to review code diffs and return structured feedback.

Focus on these areas in order of priority:
1. Security vulnerabilities (SQL injection, hardcoded secrets, insecure deserialization)
2. C#/.NET correctness (null handling, async/await misuse, IDisposable, exception handling)
3. Oracle/SQL concerns (unparameterised queries, missing indexes hint, cursor leaks)
4. Code quality and best practices (SOLID, DRY, unnecessary complexity)
5. Naming conventions and style (.NET naming standards, clarity)

Return ONLY a valid JSON object. No markdown. No explanation outside the JSON.

Schema:
{
  "summary": "string — 2-4 sentence overall assessment",
  "overallSeverity": "critical | high | medium | low | pass",
  "inlineComments": [
    {
      "filePath":  "string — relative path matching the diff",
      "line":      "number — line number in the right (new) file",
      "severity":  "critical | high | medium | low | info",
      "category":  "security | correctness | sql | quality | style",
      "comment":   "string — specific actionable feedback for this line"
    }
  ]
}`;

export function buildUserPrompt(prInfo: PrInfo, chunks: DiffChunk[]): string {
  const totalFiles = chunks.length;
  const totalLines = chunks.reduce((sum, c) => sum + c.lineCount, 0);

  const targetBranch = prInfo.targetRefName.replace('refs/heads/', '');

  const fileSections = chunks
    .map(c => {
      const truncatedNotice = c.truncated ? '\n[File truncated — only first portion shown]' : '';
      return `--- ${c.filePath} ---\n${c.diffContent}${truncatedNotice}`;
    })
    .join('\n\n');

  return `PR Title: ${prInfo.title}

PR Description: ${prInfo.description || '(none)'}

Target Branch: ${targetBranch}

Files changed (${totalFiles} files, ${totalLines} lines):

${fileSections}`;
}
