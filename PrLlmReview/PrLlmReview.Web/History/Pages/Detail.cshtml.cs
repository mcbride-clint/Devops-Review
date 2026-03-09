using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrLlmReview.Models;

namespace PrLlmReview.History.Pages;

public sealed class DetailModel : PageModel
{
    private readonly HistoryRepository _repo;

    public DetailModel(HistoryRepository repo)
    {
        _repo = repo;
    }

    public ReviewRecord? Record { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Record = await _repo.GetByIdAsync(id, ct);
        if (Record is null) return NotFound();
        return Page();
    }
}
