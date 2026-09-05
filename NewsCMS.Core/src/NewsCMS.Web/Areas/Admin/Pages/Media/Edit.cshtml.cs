using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewsCMS.Application.Content;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Media;

[Authorize(Permissions.Media.Edit)]
public class EditModel : PageModel
{
    private readonly IMediaService _media;

    public EditModel(IMediaService media) => _media = media;

    [BindProperty] public EditInput Input { get; set; } = new();
    public MediaDetailDto? Detail { get; private set; }
    public List<SelectListItem> Folders { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Detail = await _media.GetByIdAsync(id, ct);
        if (Detail is null) return NotFound();

        Input = new EditInput
        {
            Id = Detail.Id,
            Title = Detail.Title,
            AltText = Detail.AltText,
            FolderId = Detail.FolderId
        };

        await LoadFoldersAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Detail = await _media.GetByIdAsync(Input.Id, ct);
            await LoadFoldersAsync(ct);
            return Page();
        }

        var result = await _media.UpdateAsync(Input.Id, new MediaUpdateDto(Input.Title, Input.AltText, Input.FolderId), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Lỗi khi cập nhật.");
            Detail = await _media.GetByIdAsync(Input.Id, ct);
            await LoadFoldersAsync(ct);
            return Page();
        }

        TempData["Success"] = "Đã cập nhật media.";
        return RedirectToPage(new { id = Input.Id });
    }

    private async Task LoadFoldersAsync(CancellationToken ct)
    {
        var folders = await _media.GetFoldersAsync(ct);
        Folders = folders.Select(f => new SelectListItem(f.Name, f.Id.ToString())).ToList();
    }

    public class EditInput
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? AltText { get; set; }
        public Guid? FolderId { get; set; }
    }
}
