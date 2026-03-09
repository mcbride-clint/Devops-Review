using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrLlmReview.Models;

namespace PrLlmReview.Services;

/// <summary>
/// Wraps all Azure DevOps Server REST API calls required for PR review.
/// Uses a PAT (Basic auth) stored in configuration — never the webhook caller's identity.
/// </summary>
public sealed class AdoClientService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AdoClientService> _logger;

    public AdoClientService(HttpClient http, IConfiguration config, ILogger<AdoClientService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;

        var pat = _config["Ado:PersonalAccessToken"] ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    // -------------------------------------------------------------------------
    // PR Iterations
    // -------------------------------------------------------------------------

    public async Task<int> GetLatestIterationIdAsync(ReviewJob job, CancellationToken ct)
    {
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/pullRequests/{job.PullRequestId}/iterations?api-version=6.0";

        var response = await _http.GetFromJsonAsync<IterationsResponse>(url, ct)
                       ?? throw new InvalidOperationException("Empty response from iterations API");

        var latest = response.Value.LastOrDefault()
                     ?? throw new InvalidOperationException($"No iterations found for PR #{job.PullRequestId}");

        return latest.Id;
    }

    public async Task<(string HeadSha, string BaseSha)> GetIterationCommitsAsync(
        ReviewJob job, int iterationId, CancellationToken ct)
    {
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/pullRequests/{job.PullRequestId}/iterations/{iterationId}?api-version=6.0";

        var iteration = await _http.GetFromJsonAsync<IterationDetail>(url, ct)
                        ?? throw new InvalidOperationException("Empty iteration detail response");

        var head = iteration.SourceRefCommit?.CommitId ?? string.Empty;
        var base_ = iteration.CommonRefCommit?.CommitId ?? iteration.TargetRefCommit?.CommitId ?? string.Empty;
        return (head, base_);
    }

    // -------------------------------------------------------------------------
    // Changed files
    // -------------------------------------------------------------------------

    public async Task<List<ChangedFileInfo>> GetChangedFilesAsync(
        ReviewJob job, int iterationId, CancellationToken ct)
    {
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/pullRequests/{job.PullRequestId}/iterations/{iterationId}/changes?api-version=6.0";

        var response = await _http.GetFromJsonAsync<ChangesResponse>(url, ct)
                       ?? throw new InvalidOperationException("Empty changes response");

        return response.ChangeEntries
            .Where(e => e.Item?.Path != null)
            .Select(e => new ChangedFileInfo(e.Item!.Path!, MapChangeType(e.ChangeType)))
            .ToList();
    }

    // -------------------------------------------------------------------------
    // File content at a commit
    // -------------------------------------------------------------------------

    public async Task<string> GetFileAtCommitAsync(
        ReviewJob job, string filePath, string commitSha, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(commitSha)) return string.Empty;

        var encodedPath = Uri.EscapeDataString(filePath);
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/items?path={encodedPath}&versionType=commit&version={commitSha}&api-version=6.0";

        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch file {Path} at {Sha}", filePath, commitSha);
            return string.Empty;
        }
    }

    // -------------------------------------------------------------------------
    // Post comment threads
    // -------------------------------------------------------------------------

    public async Task PostThreadAsync(ReviewJob job, object threadPayload, CancellationToken ct)
    {
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/pullRequests/{job.PullRequestId}/threads?api-version=6.0";

        var json = JsonSerializer.Serialize(threadPayload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
    }

    // -------------------------------------------------------------------------
    // Check for existing review thread (duplicate detection)
    // -------------------------------------------------------------------------

    public async Task<bool> HasExistingReviewThreadAsync(ReviewJob job, CancellationToken ct)
    {
        var url = $"{job.CollectionUrl}/{job.ProjectName}/_apis/git/repositories/{job.RepositoryId}" +
                  $"/pullRequests/{job.PullRequestId}/threads?api-version=6.0";

        var response = await _http.GetFromJsonAsync<ThreadsResponse>(url, ct);
        return response?.Value.Any(t =>
            t.Comments.Any(c => c.Content?.Contains("🤖 LLM Code Review") == true)) ?? false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string MapChangeType(int changeType) => changeType switch
    {
        1 => "add",
        2 => "edit",
        4 => "delete",
        8 => "rename",
        _ => "unknown"
    };

    // -------------------------------------------------------------------------
    // JSON shapes
    // -------------------------------------------------------------------------

    private sealed class IterationsResponse
    {
        [JsonPropertyName("value")] public List<IterationSummary> Value { get; init; } = [];
    }

    private sealed class IterationSummary
    {
        [JsonPropertyName("id")] public int Id { get; init; }
    }

    private sealed class IterationDetail
    {
        [JsonPropertyName("sourceRefCommit")] public CommitRef? SourceRefCommit { get; init; }
        [JsonPropertyName("targetRefCommit")] public CommitRef? TargetRefCommit { get; init; }
        [JsonPropertyName("commonRefCommit")] public CommitRef? CommonRefCommit { get; init; }
    }

    private sealed class CommitRef
    {
        [JsonPropertyName("commitId")] public string CommitId { get; init; } = string.Empty;
    }

    private sealed class ChangesResponse
    {
        [JsonPropertyName("changeEntries")] public List<ChangeEntry> ChangeEntries { get; init; } = [];
    }

    private sealed class ChangeEntry
    {
        [JsonPropertyName("changeType")] public int ChangeType { get; init; }
        [JsonPropertyName("item")] public ItemRef? Item { get; init; }
    }

    private sealed class ItemRef
    {
        [JsonPropertyName("path")] public string? Path { get; init; }
    }

    private sealed class ThreadsResponse
    {
        [JsonPropertyName("value")] public List<ThreadSummary> Value { get; init; } = [];
    }

    private sealed class ThreadSummary
    {
        [JsonPropertyName("comments")] public List<CommentSummary> Comments { get; init; } = [];
    }

    private sealed class CommentSummary
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}

public sealed record ChangedFileInfo(string Path, string ChangeType);
