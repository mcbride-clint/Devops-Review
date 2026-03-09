using System.Text.Json.Serialization;

namespace PrLlmReview.Models;

public sealed class LlmReviewResult
{
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("overallSeverity")]
    public string OverallSeverity { get; init; } = "pass";

    [JsonPropertyName("inlineComments")]
    public List<InlineComment> InlineComments { get; init; } = [];
}
