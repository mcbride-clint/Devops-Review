namespace PrLlmReview.Models;

public sealed class ReviewJob
{
    public ReviewJob(AdoWebhookPayload payload)
    {
        PullRequestId  = payload.Resource.PullRequestId;
        Title          = payload.Resource.Title;
        Description    = payload.Resource.Description;
        SourceRefName  = payload.Resource.SourceRefName;
        TargetRefName  = payload.Resource.TargetRefName;
        AuthorName     = payload.Resource.CreatedBy?.DisplayName ?? string.Empty;
        RepositoryId   = payload.Resource.Repository.Id;
        RepositoryName = payload.Resource.Repository.Name;
        ProjectId      = payload.ResourceContainers.Project.Id;
        ProjectName    = payload.ResourceContainers.Project.Name;
        CollectionUrl  = payload.ResourceContainers.Collection.BaseUrl;
    }

    public int    PullRequestId  { get; }
    public string Title          { get; }
    public string Description    { get; }
    public string SourceRefName  { get; }
    public string TargetRefName  { get; }
    public string AuthorName     { get; }
    public string RepositoryId   { get; }
    public string RepositoryName { get; }
    public string ProjectId      { get; }
    public string ProjectName    { get; }
    public string CollectionUrl  { get; }
}
