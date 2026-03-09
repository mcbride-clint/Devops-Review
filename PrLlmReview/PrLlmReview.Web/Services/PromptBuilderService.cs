using PrLlmReview.Models;

namespace PrLlmReview.Services;

public sealed class PromptBuilderService
{
    public const string SystemPrompt = """
        You are an expert .NET and Oracle code reviewer.
        Your job is to review code diffs and return structured feedback.

        Focus on these areas in order of priority:
        1. Security vulnerabilities (SQL injection, hardcoded secrets, insecure deserialization)
        2. C#/.NET correctness (null handling, async/await misuse, IDisposable, exceptions)
        3. Oracle/SQL concerns (unparameterised queries, cursor leaks, missing bind variables)
        4. Code quality and best practices (SOLID, DRY, unnecessary complexity)
        5. Naming conventions and style (.NET naming standards, clarity)

        Return ONLY a valid JSON object. No markdown. No explanation outside the JSON.

        {
          "summary": "string — 2-4 sentence overall assessment",
          "overallSeverity": "critical | high | medium | low | pass",
          "inlineComments": [
            {
              "filePath":  "string",
              "line":      "number",
              "severity":  "critical | high | medium | low | info",
              "category":  "security | correctness | sql | quality | style",
              "comment":   "string"
            }
          ]
        }
        """;

    public string BuildUserPrompt(ReviewJob job, List<DiffChunk> chunks)
    {
        var targetBranch = job.TargetRefName.Replace("refs/heads/", string.Empty);
        var totalFiles = chunks.Count;
        var totalLines = chunks.Sum(c => c.LineCount);

        var fileSections = chunks
            .Select(c =>
            {
                var truncatedNote = c.Truncated ? "\n[File truncated — only first portion shown]" : string.Empty;
                return $"--- {c.FilePath} ---\n{c.DiffContent}{truncatedNote}";
            });

        var diffBlock = string.Join("\n\n", fileSections);

        return $"""
            PR Title: {job.Title}

            PR Description: {(string.IsNullOrWhiteSpace(job.Description) ? "(none)" : job.Description)}

            Target Branch: {targetBranch}

            Files changed ({totalFiles} files, {totalLines} lines):

            {diffBlock}
            """;
    }
}
