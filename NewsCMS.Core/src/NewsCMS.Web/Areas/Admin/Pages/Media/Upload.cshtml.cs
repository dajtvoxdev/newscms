using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Content;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Media;

/// <summary>
/// POST /admin/Media/Upload — used by TinyMCE, featured-image picker, and upload-manager.js.
/// Delegates to IMediaService; returns { location, id, fileName, size, width, height, kind }.
/// </summary>
[IgnoreAntiforgeryToken(Order = 1001)]
[Authorize(Permissions.Media.Upload)]
[RequestSizeLimit(200L * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 200L * 1024 * 1024)]
public class UploadModel : PageModel
{
    private readonly IMediaService _media;
    private readonly UserManager<AppUser> _users;

    public UploadModel(IMediaService media, UserManager<AppUser> users)
    {
        _media = media;
        _users = users;
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(
        [FromForm] IFormFile? file,
        [FromForm] Guid? folderId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Chưa chọn file." });

        var userId = Guid.Parse(_users.GetUserId(User)!);
        await using var stream = file.OpenReadStream();

        var result = await _media.CreateFromUploadAsync(
            stream, file.FileName, file.ContentType, file.Length, userId, folderId, ct);

        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });

        var dto = result.Value!;
        return new JsonResult(new
        {
            location = dto.Location,
            id = dto.Id,
            fileName = dto.FileName,
            size = dto.Size,
            width = dto.Width,
            height = dto.Height,
            kind = dto.Kind
        });
    }
}
