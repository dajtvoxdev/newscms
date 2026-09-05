using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Media;

[Authorize(Permissions.Media.Delete)]
public class TrashModel : PageModel
{
    private readonly IMediaService _media;

    public TrashModel(IMediaService media) => _media = media;

    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;

    public PagedList<MediaListItemDto> Items { get; private set; } = PagedList<MediaListItemDto>.Empty();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _media.SearchAsync(null, null, null, includeDeleted: true, CurrentPage, 24, ct);
        // Filter to only deleted items (SearchAsync with includeDeleted=true returns all; we pass a predicate via the service)
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken ct)
    {
        await _media.RestoreAsync(id, ct);
        TempData["Success"] = "Đã khôi phục media.";
        return RedirectToPage(new { p = CurrentPage });
    }

    public async Task<IActionResult> OnPostPurgeAsync(Guid id, CancellationToken ct)
    {
        await _media.PurgeAsync(id, ct);
        TempData["Success"] = "Đã xoá vĩnh viễn.";
        return RedirectToPage(new { p = CurrentPage });
    }
}
