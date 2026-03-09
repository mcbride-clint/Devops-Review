using PrLlmReview.Models;

namespace PrLlmReview.Services;

/// <summary>
/// Posts the LLM review summary and inline comments to the ADO PR via the REST API.
/// </summary>
public sealed class CommentPosterService
{
    private static readonly IReadOnlyDictionary<string, string> SeverityBadge =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = "🔴 Critical",
            ["high"]     = "🟠 High",
            ["medium"]   = "🟡 Medium",
            ["low"]      = "🟢 Low",
            ["info"]     = "ℹ️ Info",
        };

    private readonly AdoClientService _ado;
    private readonly IConfiguration _config;
    private readonly ILogger<CommentPosterService> _logger;

    public CommentPosterService(
        AdoClientService ado,
        IConfiguration config,
        ILogger<CommentPosterService> logger)
    {
        _ado    = ado;
        _config = config;
        _logger = logger;
    }

    public async Task PostSummaryAsync(
        ReviewJob job,
        LlmReviewResult result,
        int fileCount,
        List<string> skippedFiles,
        CancellationToken ct)
    {
        var model   = _config["Llm:Model"] ?? "LLM";
        var content = BuildSummaryMarkdown(result, fileCount, skippedFiles, model);

        var thread = new
        {
            comments = new[] { new { parentCommentId = 0, content, commentType = 1 } },
            status   = 1
        };

        await _ado.PostThreadAsync(job, thread, ct);
    }

    public async Task PostInlineCommentsAsync(
        ReviewJob job,
        List<InlineComment> comments,
        CancellationToken ct)
    {
        foreach (var comment in comments)
        {
            var content = BuildInlineContent(comment);

            var thread = new
            {
                comments = new[] { new { parentCommentId = 0, content, commentType = 1 } },
                status   = 1,
                threadContext = new
                {
                    filePath       = comment.FilePath,
                    rightFileStart = new { line = comment.Line, offset = 1 },
                    rightFileEnd   = new { line = comment.Line, offset = 1 },
                }
            };

            try
            {
                await _ado.PostThreadAsync(job, thread, ct);
            }
            catch (Exception ex)
            {
                // A bad line number shouldn't abort all other comments
                _logger.LogWarning(ex,
                    "Failed to post inline comment on {File}:{Line}", comment.FilePath, comment.Line);
            }
        }
    }

    private static string BuildSummaryMarkdown(
        LlmReviewResult result,
        int fileCount,
        List<string> skippedFiles,
        string model)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        var counts    = CountBySeverity(result.InlineComments);

        var skippedSection = skippedFiles.Count > 0
            ? $"\n\n> **Note:** {skippedFiles.Count} file(s) exceeded the review limit and were skipped:\n" +
              string.Join("\n", skippedFiles.Select(f => $"> - `{f}`"))
            : string.Empty;

        return $"""
            ## 🤖 LLM Code Review

            > Reviewed by {model} • {fileCount} files • {timestamp}

            ### Overall Assessment

            {result.Summary}

            ### Findings by Severity

            | Severity | Count |
            |----------|-------|
            | 🔴 Critical | {counts["critical"]} |
            | 🟠 High     | {counts["high"]} |
            | 🟡 Medium   | {counts["medium"]} |
            | 🟢 Low      | {counts["low"]} |
            | ℹ️ Info     | {counts["info"]} |

            Inline comments have been added to the changed lines above.{skippedSection}
            """;
    }

    private static string BuildInlineContent(InlineComment comment)
    {
        var badge    = SeverityBadge.TryGetValue(comment.Severity, out var b) ? b : comment.Severity;
        var category = char.ToUpperInvariant(comment.Category[0]) + comment.Category[1..];
        return $"**[{badge} • {category}]** {comment.Comment}";
    }

    private static Dictionary<string, int> CountBySeverity(List<InlineComment> comments)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = 0, ["high"] = 0, ["medium"] = 0, ["low"] = 0, ["info"] = 0
        };
        foreach (var c in comments)
            if (counts.ContainsKey(c.Severity))
                counts[c.Severity]++;
        return counts;
    }
}
