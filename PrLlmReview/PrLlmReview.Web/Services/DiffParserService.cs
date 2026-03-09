using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PrLlmReview.Models;
using System.Text;

namespace PrLlmReview.Services;

public sealed class DiffChunk
{
    public string FilePath    { get; init; } = string.Empty;
    public string DiffContent { get; init; } = string.Empty;
    public int    LineCount   { get; init; }
    public bool   Truncated   { get; init; }
}

/// <summary>
/// Builds unified-style diff strings from base/head file content using DiffPlex.
/// </summary>
public sealed class DiffParserService
{
    private const int ChunkSizeLines = 3000;

    public DiffChunk? BuildFileDiff(string filePath, string baseContent, string headContent, int maxLinesPerFile)
    {
        var diff = InlineDiffBuilder.Diff(baseContent, headContent);

        var sb = new StringBuilder();
        int lineNumber = 0;

        foreach (var line in diff.Lines)
        {
            lineNumber++;
            var prefix = line.Type switch
            {
                ChangeType.Inserted => "+",
                ChangeType.Deleted  => "-",
                _                   => " "
            };
            sb.AppendLine($"{prefix}{line.Text}");
        }

        var lines = sb.ToString().Split('\n');
        var truncated = false;
        string[] finalLines;

        if (lines.Length > maxLinesPerFile)
        {
            finalLines = [.. lines.Take(maxLinesPerFile),
                $"[... truncated: file exceeds {maxLinesPerFile} lines ...]"];
            truncated = true;
        }
        else
        {
            finalLines = lines;
        }

        var content = string.Join('\n', finalLines).Trim();
        if (string.IsNullOrWhiteSpace(content)) return null;

        return new DiffChunk
        {
            FilePath    = filePath,
            DiffContent = content,
            LineCount   = finalLines.Length,
            Truncated   = truncated,
        };
    }

    /// <summary>
    /// Splits a list of file chunks into batches that fit within the LLM context window.
    /// </summary>
    public List<List<DiffChunk>> SplitIntoLlmBatches(List<DiffChunk> chunks)
    {
        var batches = new List<List<DiffChunk>>();
        var current = new List<DiffChunk>();
        var currentLines = 0;

        foreach (var chunk in chunks)
        {
            if (currentLines + chunk.LineCount > ChunkSizeLines && current.Count > 0)
            {
                batches.Add(current);
                current = [];
                currentLines = 0;
            }
            current.Add(chunk);
            currentLines += chunk.LineCount;
        }

        if (current.Count > 0) batches.Add(current);
        return batches;
    }
}
