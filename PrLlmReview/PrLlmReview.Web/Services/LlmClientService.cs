using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrLlmReview.Models;

namespace PrLlmReview.Services;

/// <summary>
/// Sends prompts to an onsite OpenAI-compatible /v1/chat/completions endpoint
/// and parses the structured JSON response.
/// </summary>
public sealed class LlmClientService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<LlmClientService> _logger;

    public LlmClientService(HttpClient http, IConfiguration config, ILogger<LlmClientService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;

        var timeoutSeconds = _config.GetValue<int>("Llm:TimeoutSeconds", 120);
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var apiKey = _config["Llm:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<LlmReviewResult> ReviewAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var baseUrl = _config["Llm:BaseUrl"]!.TrimEnd('/');
        var url     = $"{baseUrl}/chat/completions";
        var model   = _config["Llm:Model"]!;
        var maxTokens  = _config.GetValue<int>("Llm:MaxTokens", 4096);
        var temperature = _config.GetValue<double>("Llm:Temperature", 0.2);

        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt },
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"LLM request timed out after {_http.Timeout.TotalSeconds}s", ex);
        }

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var completion = JsonSerializer.Deserialize<CompletionResponse>(responseJson);
        var rawText = completion?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return ParseLlmResponse(rawText);
    }

    private LlmReviewResult ParseLlmResponse(string text)
    {
        var cleaned = text
            .Trim()
            .TrimStart('`')
            .TrimEnd('`');

        // Strip json fence if model added one despite instructions
        if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[4..].TrimStart();

        try
        {
            var result = JsonSerializer.Deserialize<LlmReviewResult>(cleaned);
            if (result is not null) return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LLM response could not be parsed as JSON");
        }

        return new LlmReviewResult
        {
            Summary = $"LLM returned a response that could not be parsed as JSON.\n\nRaw response:\n\n{text}",
            OverallSeverity = "pass",
            InlineComments = [],
        };
    }

    public static LlmReviewResult MergeResults(List<LlmReviewResult> results)
    {
        if (results.Count == 0) return new LlmReviewResult { Summary = string.Empty };
        if (results.Count == 1) return results[0];

        var severityOrder = new[] { "critical", "high", "medium", "low", "pass" };
        var worstIndex = results
            .Select(r => Array.IndexOf(severityOrder, r.OverallSeverity))
            .Where(i => i >= 0)
            .DefaultIfEmpty(severityOrder.Length - 1)
            .Min();

        return new LlmReviewResult
        {
            Summary         = string.Join("\n\n", results.Select((r, i) => $"**Chunk {i + 1}:** {r.Summary}")),
            OverallSeverity = severityOrder[worstIndex],
            InlineComments  = results.SelectMany(r => r.InlineComments).ToList(),
        };
    }

    // JSON shapes
    private sealed class CompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; init; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
