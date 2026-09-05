using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Media;

[Authorize(Permissions.Media.View)]
public class IndexModel : PageModel
{
    private readonly IMediaService _media;
    private readonly AppDbContext _db;

    public IndexModel(IMediaService media, AppDbContext db)
    {
        _media = media;
        _db = db;
    }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "kind")] public string? KindFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "folder")] public Guid? FolderFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;

    public PagedList<MediaListItemDto> Items { get; private set; } = PagedList<MediaListItemDto>.Empty();
    public IReadOnlyList<MediaFolderDto> Folders { get; private set; } = Array.Empty<MediaFolderDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var kind = Enum.TryParse<MediaKind>(KindFilter, true, out var k) ? k : (MediaKind?)null;
        Items = await _media.SearchAsync(Keyword, kind, FolderFilter, false, CurrentPage, 24, ct);
        Folders = await _media.GetFoldersAsync(ct);
    }

    // JSON endpoint consumed by the library-picker modal.
    public async Task<IActionResult> OnGetListAsync(
        string? q, string? kind, Guid? folder, int p = 1, CancellationToken ct = default)
    {
        var kindEnum = Enum.TryParse<MediaKind>(kind, true, out var k) ? k : (MediaKind?)null;
        var result = await _media.SearchAsync(q, kindEnum, folder, false, p, 24, ct);
        return new JsonResult(result);
    }

    /// <summary>
    /// Poll trạng thái upload tus: client gọi sau khi PATCH cuối trả 204 để nhận
    /// kết quả xử lý (Media row + poster). Row chưa tồn tại coi như đang xử lý vì
    /// OnFileCompleteAsync có thể chưa kịp ghi.
    /// </summary>
    public async Task<IActionResult> OnGetTusStatusAsync(Guid tusId, CancellationToken ct)
    {
        // Endpoint upload yêu cầu Media.Upload — poll cũng cần cùng quyền.
        if (!User.HasClaim(Permissions.Prefix, Permissions.Media.Upload))
            return new JsonResult(new { status = "failed", error = "Không có quyền tải lên." }) { StatusCode = 403 };

        var session = await _db.MediaUploadSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TusFileId == tusId.ToString("N") || s.TusFileId == tusId.ToString(), ct);

        if (session is null || session.Status == "processing")
            return new JsonResult(new { status = "processing" });

        if (session.Status == "failed")
            return new JsonResult(new { status = "failed", error = session.Error ?? "Upload thất bại." });

        if (session.MediaId.HasValue)
        {
            var detail = await _media.GetByIdAsync(session.MediaId.Value, ct);
            if (detail != null)
                return new JsonResult(new
                {
                    status = "completed",
                    location = detail.FilePath,
                    id = detail.Id,
                    fileName = detail.FileName,
                    size = detail.Size,
                    width = detail.Width,
                    height = detail.Height,
                    kind = detail.Kind,
                    posterUrl = (await _db.Medias.AsNoTracking()
                        .Where(m => m.Id == detail.Id)
                        .Select(m => m.PosterUrl)
                        .FirstOrDefaultAsync(ct)) ?? detail.FilePath
                });
        }

        return new JsonResult(new { status = "failed", error = "Không tìm thấy media đã upload." });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        await _media.SoftDeleteAsync(id, ct);
        TempData["Success"] = "Đã chuyển file vào thùng rác.";
        return RedirectToPage(new { q = Keyword, kind = KindFilter, folder = FolderFilter, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostCreateFolderAsync(string name, Guid? parentId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _media.CreateFolderAsync(name.Trim(), parentId, ct);
            TempData["Success"] = "Đã tạo thư mục.";
        }
        return RedirectToPage(new { q = Keyword, kind = KindFilter, folder = FolderFilter, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostDeleteFolderAsync(Guid id, CancellationToken ct)
    {
        await _media.DeleteFolderAsync(id, ct);
        TempData["Success"] = "Đã xoá thư mục.";
        return RedirectToPage(new { q = Keyword, kind = KindFilter, folder = FolderFilter, p = CurrentPage });
    }
}
