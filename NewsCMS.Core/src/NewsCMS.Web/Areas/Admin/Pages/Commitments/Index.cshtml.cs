using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.Engagement;
using NewsCMS.Application.Engagement.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Commitments;

[Authorize(Permissions.Engagement.ViewCommitments)]
public class IndexModel : PageModel
{
    private readonly ICommitmentService _service;

    public IndexModel(ICommitmentService service)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Keyword { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int CurrentPage { get; set; } = 1;

    public PagedList<CommitmentAdminItemDto> Items { get; private set; } = PagedList<CommitmentAdminItemDto>.Empty();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _service.GetForAdminAsync(Keyword, CurrentPage, 50, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, CancellationToken ct)
    {
        var result = await _service.ToggleHiddenAsync(id, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
        }
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }
}
