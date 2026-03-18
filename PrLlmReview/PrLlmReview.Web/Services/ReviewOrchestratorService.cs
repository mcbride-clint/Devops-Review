using System.Text.Json;
using PrLlmReview.History;
using PrLlmReview.Models;

namespace PrLlmReview.Services;

/// <summary>
/// Coordinates the end-to-end review flow for a single PR job:
/// fetch diff → filter → build prompts → call LLM → post comments → persist history.
/// </summary>
public sealed class ReviewOrchestratorService
{
    private readonly AdoClientService _ado;
    private readonly DiffParserService _diffParser;
    private readonly FileFilterService _fileFilter;
    private readonly PromptBuilderService _promptBuilder;
    private readonly LlmClientService _llm;
    private readonly CommentPosterService _poster;
    private readonly HistoryRepository? _history;
    private readonly IConfiguration _config;
    private readonly ILogger<ReviewOrchestratorService> _logger;

    public ReviewOrchestratorService(
        AdoClientService ado,
        DiffParserService diffParser,
        FileFilterService fileFilter,
        PromptBuilderService promptBuilder,
        LlmClientService llm,
        CommentPosterService poster,
        IConfiguration config,
        ILogger<ReviewOrchestratorService> logger,
        HistoryRepository? history = null)
    {
        _ado           = ado;
        _diffParser    = diffParser;
        _fileFilter    = fileFilter;
        _promptBuilder = promptBuilder;
        _llm           = llm;
        _poster        = poster;
        _config        = config;
        _logger        = logger;
        _history       = history;
    }

    public async Task RunAsync(ReviewJob job, CancellationToken ct)
    {
        // Duplicate detection — skip if we already reviewed this PR
        if (await _ado.HasExistingReviewThreadAsync(job, ct))
        {
            _logger.LogInformation(
                "PR #{PrId} already has a review thread — skipping duplicate.", job.PullRequestId);
            return;
        }

        // 1. Fetch latest iteration
        _logger.LogDebug("Fetching iteration for PR #{PrId}", job.PullRequestId);
        int iterationId = await _ado.GetLatestIterationIdAsync(job, ct);
        var (headSha, baseSha) = await _ado.GetIterationCommitsAsync(job, iterationId, ct);

        // 2. Changed files
        _logger.LogDebug("Fetching changed files...");
        var allFiles = await _ado.GetChangedFilesAsync(job, iterationId, ct);
        _logger.LogInformation("PR #{PrId} has {Count} changed file(s)", job.PullRequestId, allFiles.Count);

        // 3. Filter
        var filterResult = _fileFilter.Filter(allFiles);
        _logger.LogInformation(
            "Included: {Inc}, Skipped (cap): {Skip}, Excluded (pattern): {Excl}",
            filterResult.Included.Count, filterResult.Skipped.Count, filterResult.Excluded.Count);

        if (filterResult.Included.Count == 0)
        {
            _logger.LogInformation("No reviewable files after filtering — nothing to post.");
            return;
        }

        // 4. Build diffs
        var maxLinesPerFile = _config.GetValue<int>("Review:MaxLinesPerFile", 300);
        var diffChunks = new List<DiffChunk>();

        foreach (var file in filterResult.Included)
        {
            var baseContent = file.ChangeType == "add"
                ? string.Empty
                : await _ado.GetFileAtCommitAsync(job, file.Path, baseSha, ct);

            var headContent = file.ChangeType == "delete"
                ? string.Empty
                : await _ado.GetFileAtCommitAsync(job, file.Path, headSha, ct);

            var chunk = _diffParser.BuildFileDiff(file.Path, baseContent, headContent, maxLinesPerFile);
            if (chunk is not null) diffChunks.Add(chunk);
        }

        if (diffChunks.Count == 0)
        {
            _logger.LogInformation("No diff content generated — nothing to review.");
            return;
        }

        // 5. Split into LLM batches and review
        var batches = _diffParser.SplitIntoLlmBatches(diffChunks);
        _logger.LogInformation(
            "Sending {Files} file diff(s) in {Batches} LLM chunk(s)",
            diffChunks.Count, batches.Count);

        var batchResults = new List<LlmReviewResult>();

        for (int i = 0; i < batches.Count; i++)
        {
            _logger.LogInformation("Calling LLM — chunk {N}/{Total}", i + 1, batches.Count);
            try
            {
                var userPrompt = _promptBuilder.BuildUserPrompt(job, batches[i]);
                var result = await _llm.ReviewAsync(_promptBuilder.BuildSystemPrompt(), userPrompt, ct);
                batchResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM call failed for chunk {N}", i + 1);
                batchResults.Add(new LlmReviewResult
                {
                    Summary         = $"LLM review unavailable for chunk {i + 1}: {ex.Message}",
                    OverallSeverity = "pass",
                    InlineComments  = [],
                });
            }
        }

        var merged      = LlmClientService.MergeResults(batchResults);
        var skippedPaths = filterResult.Skipped.Select(f => f.Path).ToList();

        // 6. Post comments
        _logger.LogInformation("Posting summary comment...");
        await _poster.PostSummaryAsync(job, merged, filterResult.Included.Count, skippedPaths, ct);

        if (merged.InlineComments.Count > 0)
        {
            _logger.LogInformation("Posting {Count} inline comment(s)...", merged.InlineComments.Count);
            await _poster.PostInlineCommentsAsync(job, merged.InlineComments, ct);
        }

        // 7. Persist to history
        if (_history is not null)
        {
            var record = new ReviewRecord
            {
                ReviewedAt      = DateTime.UtcNow.ToString("o"),
                ProjectName     = job.ProjectName,
                RepositoryName  = job.RepositoryName,
                PrId            = job.PullRequestId,
                PrTitle         = job.Title,
                AuthorName      = job.AuthorName,
                TargetBranch    = job.TargetRefName.Replace("refs/heads/", string.Empty),
                FilesReviewed   = filterResult.Included.Count,
                OverallSeverity = merged.OverallSeverity,
                SummaryText     = merged.Summary,
                FullResultJson  = JsonSerializer.Serialize(merged),
            };

            await _history.SaveAsync(record, ct);
            _logger.LogDebug("Review persisted to history.");
        }
    }
}
