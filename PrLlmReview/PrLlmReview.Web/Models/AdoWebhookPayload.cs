using System.Text.Json.Serialization;

namespace PrLlmReview.Models;

public sealed class AdoWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("resource")]
    public PrResource Resource { get; init; } = new();

    [JsonPropertyName("resourceContainers")]
    public ResourceContainers ResourceContainers { get; init; } = new();
}

public sealed class PrResource
{
    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("sourceRefName")]
    public string SourceRefName { get; init; } = string.Empty;

    [JsonPropertyName("targetRefName")]
    public string TargetRefName { get; init; } = string.Empty;

    [JsonPropertyName("createdBy")]
    public IdentityRef? CreatedBy { get; init; }

    [JsonPropertyName("repository")]
    public RepositoryRef Repository { get; init; } = new();

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

public sealed class IdentityRef
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class RepositoryRef
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ResourceContainers
{
    [JsonPropertyName("project")]
    public ContainerRef Project { get; init; } = new();

    [JsonPropertyName("collection")]
    public CollectionRef Collection { get; init; } = new();
}

public sealed class ContainerRef
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class CollectionRef
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;
}
