using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>
/// Nhãn AI trên mọi video: chữ và font. Đổi được hình thức, KHÔNG tắt được nhãn.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class LabelModel : PageModel
{
    public const string TextKey = "AiLabelOverlayText";

    /// <summary>Khớp trần upload font của AdVideo (25 MB) — chặn sớm, khỏi đẩy cả file đi rồi mới bị từ chối.</summary>
    private const long MaxFontBytes = 25L * 1024 * 1024;

    private readonly IAdVideoAdminClient _admin;

    public LabelModel(IAdVideoAdminClient admin) => _admin = admin;

    public string? LabelText { get; private set; }
    public bool LabelIsProvisional { get; private set; }
    public AdVideoLabelFontDto? Font { get; private set; }
    public string? LoadError { get; private set; }

    public async Task OnGetAsync()
    {
        Result<IReadOnlyList<AdVideoSettingDto>> settings = await _admin.GetSettingsAsync();
        Result<AdVideoLabelFontDto> font = await _admin.GetLabelFontAsync();

        AdVideoSettingDto? text = settings.Value?.FirstOrDefault(s => s.Key == TextKey);
        LabelText = text?.Value;
        LabelIsProvisional = text?.IsProvisional ?? false;
        Font = font.Value;
        LoadError = settings.Error ?? font.Error;
    }

    public async Task<IActionResult> OnPostTextAsync(string? text)
    {
        Result<AdVideoSettingDto> result = await _admin.UpdateSettingAsync(TextKey, text ?? string.Empty);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Chữ nhãn mới: \"{result.Value!.Value}\". Áp dụng cho video dựng từ giờ."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFontAsync(IFormFile? font)
    {
        if (font is null || font.Length == 0)
        {
            TempData["Error"] = "Chọn một file font .ttf / .otf / .ttc.";
            return RedirectToPage();
        }

        if (font.Length > MaxFontBytes)
        {
            TempData["Error"] = $"Font nặng {font.Length / 1024 / 1024} MB, tối đa 25 MB.";
            return RedirectToPage();
        }

        await using Stream stream = font.OpenReadStream();
        Result<AdVideoLabelFontDto> result = await _admin.UploadLabelFontAsync(stream, font.FileName);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã dùng font {font.FileName} cho nhãn AI (đủ glyph tiếng Việt)."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetFontAsync()
    {
        Result result = await _admin.ResetLabelFontAsync();

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã quay về font cấu hình trên máy chạy AdVideo."
            : result.Error;

        return RedirectToPage();
    }
}
