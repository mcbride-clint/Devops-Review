using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrLlmReview.Models;

namespace PrLlmReview.History.Pages;

public sealed class IndexModel : PageModel
{
    private readonly HistoryRepository _repo;

    public IndexModel(HistoryRepository repo)
    {
        _repo = repo;
    }

    [BindProperty(SupportsGet = true)] public string? Repo         { get; set; }
    [BindProperty(SupportsGet = true)] public string? TitleKeyword { get; set; }
    [BindProperty(SupportsGet = true)] public string? Severity     { get; set; }
    [BindProperty(SupportsGet = true)] public string? FromDate     { get; set; }
    [BindProperty(SupportsGet = true)] public string? ToDate       { get; set; }
    [BindProperty(SupportsGet = true)] public int     Page         { get; set; } = 1;

    public List<ReviewRecord> Records   { get; private set; } = [];
    public int                TotalCount { get; private set; }
    public int                TotalPages { get; private set; }

    private const int PageSize = 25;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Page < 1) Page = 1;
        (Records, TotalCount) = await _repo.SearchAsync(
            Repo, TitleKeyword, Severity, FromDate, ToDate, Page, PageSize, ct);
        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public string BuildQuery(int targetPage)
    {
        var parts = new List<string> { $"page={targetPage}" };
        if (!string.IsNullOrWhiteSpace(Repo))         parts.Add($"repo={Uri.EscapeDataString(Repo)}");
        if (!string.IsNullOrWhiteSpace(TitleKeyword)) parts.Add($"titleKeyword={Uri.EscapeDataString(TitleKeyword)}");
        if (!string.IsNullOrWhiteSpace(Severity))     parts.Add($"severity={Uri.EscapeDataString(Severity)}");
        if (!string.IsNullOrWhiteSpace(FromDate))     parts.Add($"fromDate={FromDate}");
        if (!string.IsNullOrWhiteSpace(ToDate))       parts.Add($"toDate={ToDate}");
        return string.Join("&", parts);
    }
}
