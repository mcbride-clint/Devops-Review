namespace PrLlmReview.Models;

public sealed class ReviewRecord
{
    public int    Id             { get; set; }
    public string ReviewedAt     { get; set; } = string.Empty;
    public string ProjectName    { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public int    PrId           { get; set; }
    public string PrTitle        { get; set; } = string.Empty;
    public string AuthorName     { get; set; } = string.Empty;
    public string TargetBranch   { get; set; } = string.Empty;
    public int    FilesReviewed  { get; set; }
    public string OverallSeverity { get; set; } = string.Empty;
    public string SummaryText    { get; set; } = string.Empty;
    public string FullResultJson { get; set; } = string.Empty;
}
