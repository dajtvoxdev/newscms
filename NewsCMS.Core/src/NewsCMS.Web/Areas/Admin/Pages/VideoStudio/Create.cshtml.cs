using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.VideoStudio;

/// <summary>
/// Form tạo video quảng cáo. Không có ô chọn provider: người làm marketing chọn chất lượng và
/// khung hình, hệ thống tự chọn model (Luật 3 của AdVideo).
/// </summary>
[Authorize(Permissions.VideoStudio.Create)]
public class CreateModel : PageModel
{
    /// <summary>Trần một ảnh (AdVideo nhận tối đa 15 MB) — chặn ở đây cho khỏi đẩy lên rồi mới bị từ chối.</summary>
    private const long MaxImageBytes = 15L * 1024 * 1024;

    private readonly IVideoStudioService _studio;
    private readonly IAdVideoConnectionService _connection;

    public CreateModel(IVideoStudioService studio, IAdVideoConnectionService connection)
    {
        _studio = studio;
        _connection = connection;
    }

    public bool IsLinked { get; private set; }
    public IReadOnlyList<VideoStudioLibraryImage> Library { get; private set; } = Array.Empty<VideoStudioLibraryImage>();

    [BindProperty]
    public CreateForm Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Images { get; set; } = [];

    public async Task OnGetAsync()
    {
        await LoadAsync();

        // Sinh khoá chống trùng lúc MỞ form: bấm "Tạo" hai lần, hay F5 sau khi gửi, đều gửi lại cùng
        // khoá — AdVideo trả lại job cũ thay vì dựng (và tính tiền) video thứ hai.
        Input.IdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.IdempotencyKey))
        {
            Input.IdempotencyKey = Guid.NewGuid().ToString("N");
        }

        if (Images.FirstOrDefault(f => f.Length > MaxImageBytes) is { } tooBig)
        {
            ModelState.AddModelError(string.Empty, $"Ảnh \"{tooBig.FileName}\" nặng quá 15 MB.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        Result<AdVideoJobDto> result = await _studio.CreateAsync(new VideoStudioCreateInput(
            Input.IdempotencyKey,
            Input.ProductName,
            Input.Prompt ?? string.Empty,
            Input.Script ?? string.Empty,
            Input.DurationSeconds,
            Input.AspectRatio,
            Input.Quality,
            Input.HasPerson,
            Input.KeepSoundEffects,
            Input.LibraryMediaIds,
            Images.Where(f => f.Length > 0).Select(f => new VideoStudioUpload(f.FileName, f.OpenReadStream)).ToList()));

        if (!result.Succeeded)
        {
            // Giữ nguyên khoá chống trùng: người dùng sửa lỗi rồi gửi lại vẫn là CÙNG một yêu cầu.
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadAsync();
            return Page();
        }

        TempData["Success"] = "Đã gửi yêu cầu dựng video. Trang này tự cập nhật tiến độ.";

        return RedirectToPage("/VideoStudio/Detail", new { id = result.Value!.JobId });
    }

    private async Task LoadAsync()
    {
        IsLinked = await _connection.IsCurrentSiteLinkedAsync();
        Library = IsLinked ? await _studio.GetLibraryImagesAsync() : Array.Empty<VideoStudioLibraryImage>();
    }

    public sealed class CreateForm
    {
        public string IdempotencyKey { get; set; } = "";

        [StringLength(200)]
        public string? ProductName { get; set; }

        [Required(ErrorMessage = "Mô tả cảnh quay là bắt buộc.")]
        [StringLength(2000)]
        public string? Prompt { get; set; }

        [Required(ErrorMessage = "Lời thoại là bắt buộc.")]
        [StringLength(5000)]
        public string? Script { get; set; }

        [Range(6, 180, ErrorMessage = "Thời lượng 6–180 giây.")]
        public int DurationSeconds { get; set; } = 15;

        [RegularExpression("^(9:16|1:1|16:9)$")]
        public string AspectRatio { get; set; } = "9:16";

        [RegularExpression("^(draft|standard)$")]
        public string Quality { get; set; } = "standard";

        public bool HasPerson { get; set; }

        public bool KeepSoundEffects { get; set; }

        public List<Guid> LibraryMediaIds { get; set; } = [];
    }
}
