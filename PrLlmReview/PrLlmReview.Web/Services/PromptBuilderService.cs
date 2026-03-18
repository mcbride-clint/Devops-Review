using PrLlmReview.Models;

namespace PrLlmReview.Services;

public sealed class PromptBuilderService
{
    private static readonly string[] DefaultFocusAreas =
    [
        "Security vulnerabilities (SQL injection, hardcoded secrets, insecure deserialization)",
        "C#/.NET correctness (null handling, async/await misuse, IDisposable, exceptions)",
        "Oracle/SQL concerns (unparameterised queries, cursor leaks, missing bind variables)",
        "Code quality and best practices (SOLID, DRY, unnecessary complexity)",
        "Naming conventions and style (.NET naming standards, clarity)",
    ];

    private readonly IConfiguration _config;

    public PromptBuilderService(IConfiguration config)
    {
        _config = config;
    }

    public string BuildSystemPrompt()
    {
        var configured = _config.GetSection("Review:FocusAreas").Get<string[]>();
        var areas = configured is { Length: > 0 } ? configured : DefaultFocusAreas;

        var numberedAreas = string.Join("\n", areas.Select((a, i) => $"{i + 1}. {a}"));

        return $"""
            You are an expert code reviewer embedded in a CI/CD pipeline.
            Your job is to review code diffs and return structured feedback.

            Focus on these areas in order of priority:
            {numberedAreas}

            Return ONLY a valid JSON object. No markdown. No explanation outside the JSON.

            {{
              "summary": "string — 2-4 sentence overall assessment",
              "overallSeverity": "critical | high | medium | low | pass",
              "inlineComments": [
                {{
                  "filePath":  "string",
                  "line":      "number",
                  "severity":  "critical | high | medium | low | info",
                  "category":  "security | correctness | sql | quality | style",
                  "comment":   "string"
                }}
              ]
            }}
            """;
    }

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
