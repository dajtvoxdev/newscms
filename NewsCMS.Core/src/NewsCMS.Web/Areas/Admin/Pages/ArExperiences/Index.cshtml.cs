using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ar;
using NewsCMS.Application.Ar.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ArExperiences;

[Authorize(Permissions.Ar.ViewAr)]
public class IndexModel : PageModel
{
    private readonly IArExperienceService _ar;

    public IndexModel(IArExperienceService ar) => _ar = ar;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Keyword { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int CurrentPage { get; set; } = 1;

    public PagedList<ArExperienceDto> Items { get; private set; } = PagedList<ArExperienceDto>.Empty();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _ar.SearchAsync(Keyword, CurrentPage, 20, ct);
    }

    [Authorize(Permissions.Ar.PublishAr)]
    public async Task<IActionResult> OnPostTogglePublishAsync(Guid id, CancellationToken ct)
    {
        var result = await _ar.TogglePublishAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật trạng thái xuất bản." : result.Error;
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }
}
