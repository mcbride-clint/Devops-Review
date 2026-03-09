using System.Text.Json.Serialization;

namespace PrLlmReview.Models;

public sealed class InlineComment
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; init; } = string.Empty;

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "info";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "quality";

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;
}
