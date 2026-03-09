using Microsoft.Extensions.FileSystemGlobbing;
using PrLlmReview.Models;

namespace PrLlmReview.Services;

/// <summary>
/// Filters changed files by glob exclusion patterns and enforces maxFilesPerReview cap.
/// </summary>
public sealed class FileFilterService
{
    private readonly IConfiguration _config;

    public FileFilterService(IConfiguration config)
    {
        _config = config;
    }

    public FilterResult Filter(List<ChangedFileInfo> files)
    {
        var maxFiles = _config.GetValue<int>("Review:MaxFilesPerReview", 20);
        var patterns = _config.GetSection("Review:ExcludePatterns").Get<string[]>() ?? [];

        var matcher = new Matcher();
        matcher.AddInclude("**/*");
        foreach (var pattern in patterns)
            matcher.AddExclude(pattern.TrimStart('/').TrimStart('*').TrimStart('*').TrimStart('/'));

        // Build a smarter matcher — use micromatch-style directly via Matcher
        var excludeMatcher = new Matcher();
        foreach (var pattern in patterns)
            excludeMatcher.AddInclude(pattern);

        var included = new List<ChangedFileInfo>();
        var excluded = new List<ChangedFileInfo>();

        foreach (var file in files)
        {
            var normPath = file.Path.TrimStart('/');
            var matchResult = excludeMatcher.Match(normPath);
            if (matchResult.HasMatches)
            {
                excluded.Add(file);
            }
            else
            {
                included.Add(file);
            }
        }

        var skipped = new List<ChangedFileInfo>();
        if (included.Count > maxFiles)
        {
            skipped.AddRange(included.Skip(maxFiles));
            included = included.Take(maxFiles).ToList();
        }

        return new FilterResult(included, skipped, excluded);
    }
}

public sealed record FilterResult(
    List<ChangedFileInfo> Included,
    List<ChangedFileInfo> Skipped,
    List<ChangedFileInfo> Excluded);
