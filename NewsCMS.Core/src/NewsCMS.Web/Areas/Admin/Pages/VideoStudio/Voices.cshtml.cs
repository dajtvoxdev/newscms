using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.VideoStudio;

/// <summary>
/// Giọng đọc của site: nghe thử giọng có sẵn, clone giọng riêng từ ghi âm mẫu, xoá giọng clone.
/// </summary>
/// <remarks>
/// Clone giọng là dùng giọng một người thật. Form bắt buộc tích xác nhận và ghi lời xác nhận của chủ
/// giọng; người bấm (tài khoản đang đăng nhập) được ghi làm người xác nhận. AdVideo lưu cả ba cùng
/// mẫu ghi âm — là bằng chứng khi có khiếu nại.
/// </remarks>
[Authorize(Permissions.VideoStudio.ManageVoices)]
public class VoicesModel : PageModel
{
    /// <summary>Khớp trần của AdVideo (<c>VoiceSampleFormat</c>): 5 file, mỗi file ≤ 10 MB.</summary>
    private const int MaxFiles = 5;
    private const long MaxFileBytes = 10L * 1024 * 1024;

    private readonly IAdVideoClient _advideo;
    private readonly IAdVideoConnectionService _connection;

    public VoicesModel(IAdVideoClient advideo, IAdVideoConnectionService connection)
    {
        _advideo = advideo;
        _connection = connection;
    }

    public bool IsLinked { get; private set; }
    public IReadOnlyList<AdVideoVoiceDto> Voices { get; private set; } = Array.Empty<AdVideoVoiceDto>();
    public string? Error { get; private set; }

    [BindProperty]
    public CloneForm Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Samples { get; set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCloneAsync()
    {
        List<IFormFile> files = Samples.Where(f => f.Length > 0).ToList();

        if (files.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Tải lên ít nhất một file ghi âm giọng mẫu.");
        }
        else if (files.Count > MaxFiles)
        {
            ModelState.AddModelError(string.Empty, $"Tối đa {MaxFiles} file ghi âm.");
        }

        if (files.FirstOrDefault(f => f.Length > MaxFileBytes) is { } tooBig)
        {
            ModelState.AddModelError(string.Empty, $"File \"{tooBig.FileName}\" nặng quá 10 MB.");
        }

        if (!Input.ConsentConfirmed)
        {
            ModelState.AddModelError(nameof(Input.ConsentConfirmed), "Phải xác nhận đã có sự đồng ý của chủ giọng.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        Result<AdVideoVoiceDto> result = await _advideo.CloneVoiceAsync(new CloneVoiceInput(
            Input.Name!,
            Input.Description,
            Input.ConsentStatement!,
            Input.ConsentConfirmed,
            User.Identity?.Name ?? "không rõ",
            files.Select(f => new AdVideoVoiceSample(f.FileName, f.OpenReadStream)).ToList()));

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadAsync();
            return Page();
        }

        TempData["Success"] = $"Đã tạo giọng \"{result.Value!.Name}\". Chọn giọng này ở form tạo video.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        Result result = await _advideo.DeleteVoiceAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã xoá giọng. Video đã làm bằng giọng này không bị ảnh hưởng."
            : result.Error;

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        IsLinked = await _connection.IsCurrentSiteLinkedAsync();

        if (!IsLinked)
        {
            return;
        }

        Result<IReadOnlyList<AdVideoVoiceDto>> voices = await _advideo.ListVoicesAsync();
        Voices = voices.Value ?? Array.Empty<AdVideoVoiceDto>();
        Error = voices.Error;
    }

    public sealed class CloneForm
    {
        [Required(ErrorMessage = "Đặt tên cho giọng.")]
        [StringLength(100)]
        public string? Name { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Ghi lời xác nhận của chủ giọng.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Lời xác nhận cần ít nhất 10 ký tự: ai là chủ giọng, đồng ý thế nào, khi nào.")]
        public string? ConsentStatement { get; set; }

        public bool ConsentConfirmed { get; set; }
    }
}
