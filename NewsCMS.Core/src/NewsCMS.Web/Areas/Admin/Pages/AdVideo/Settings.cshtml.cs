using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>Tham số vận hành của AdVideo (trần chi phí, provider mặc định, số shot song song…).</summary>
[Authorize(Roles = "SuperAdmin")]
public class SettingsModel : PageModel
{
    /// <summary>Khoá có trang riêng hoặc chỉ sửa qua đường khác — không hiện ô sửa ở đây.</summary>
    public static readonly IReadOnlySet<string> ManagedElsewhere = new HashSet<string>(StringComparer.Ordinal)
    {
        "AiLabelOverlayText",
        "AiLabelFontObjectKey",
    };

    private readonly IAdVideoAdminClient _admin;

    public SettingsModel(IAdVideoAdminClient admin) => _admin = admin;

    public IReadOnlyList<AdVideoSettingDto> Settings { get; private set; } = Array.Empty<AdVideoSettingDto>();
    public string? LoadError { get; private set; }

    public async Task OnGetAsync()
    {
        Result<IReadOnlyList<AdVideoSettingDto>> result = await _admin.GetSettingsAsync();

        Settings = result.Value ?? Array.Empty<AdVideoSettingDto>();
        LoadError = result.Error;
    }

    public async Task<IActionResult> OnPostAsync(string key, string? value)
    {
        Result<AdVideoSettingDto> result = await _admin.UpdateSettingAsync(key, value ?? string.Empty);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"{key} = {result.Value!.Value}. API và Worker nhận giá trị mới trong vài giây."
            : result.Error;

        return RedirectToPage();
    }
}
