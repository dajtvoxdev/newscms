using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>
/// Giọng có sẵn cho mọi site: thêm tay, nhập từ thư viện giọng của engine, sửa, gỡ. Kèm số giọng clone theo site.
/// </summary>
/// <remarks>
/// Thứ tự có nghĩa: giọng có sẵn đầu tiên (thứ tự nhỏ nhất) của một engine là giọng mặc định khi người
/// tạo video không chọn giọng. Engine chưa có giọng có sẵn nào thì video không chọn giọng sẽ dừng ở
/// bước giọng đọc với lời nhắc thêm giọng ở đây.
/// </remarks>
[Authorize(Roles = "SuperAdmin")]
public class VoicesModel : PageModel
{
    private readonly IAdVideoAdminClient _admin;

    public VoicesModel(IAdVideoAdminClient admin) => _admin = admin;

    public IReadOnlyList<AdVideoVoicePresetDto> Presets { get; private set; } = Array.Empty<AdVideoVoicePresetDto>();
    public IReadOnlyList<AdVideoClonedVoiceStatsDto> ClonedStats { get; private set; } = Array.Empty<AdVideoClonedVoiceStatsDto>();
    public IReadOnlyList<AdVideoProviderVoiceDto>? Library { get; private set; }
    public string? LoadError { get; private set; }
    public string? LibraryError { get; private set; }

    /// <summary>Engine đang xem thư viện (<c>?provider=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Provider { get; set; }

    public async Task OnGetAsync()
    {
        Result<IReadOnlyList<AdVideoVoicePresetDto>> presets = await _admin.GetVoicePresetsAsync();
        Presets = presets.Value ?? Array.Empty<AdVideoVoicePresetDto>();
        LoadError = presets.Error;

        if (presets.Succeeded)
        {
            Result<IReadOnlyList<AdVideoClonedVoiceStatsDto>> stats = await _admin.GetClonedVoiceStatsAsync();
            ClonedStats = stats.Value ?? Array.Empty<AdVideoClonedVoiceStatsDto>();
        }

        if (!string.IsNullOrWhiteSpace(Provider))
        {
            Result<IReadOnlyList<AdVideoProviderVoiceDto>> library = await _admin.GetProviderVoiceLibraryAsync(Provider);
            Library = library.Value;
            LibraryError = library.Error;
        }
    }

    public async Task<IActionResult> OnPostAddAsync(
        string? name, string? description, string? provider, string? providerVoiceId, string? previewUrl, int? sortOrder, string? returnProvider)
    {
        Result<AdVideoVoicePresetDto> result = await _admin.AddVoicePresetAsync(
            new AdVideoVoicePresetInput(name, description, provider, providerVoiceId, previewUrl, sortOrder, true));

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã thêm giọng \"{result.Value!.Name}\". Các site thấy ngay trong form tạo video."
            : result.Error;

        return RedirectToPage(new { provider = returnProvider });
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        Guid id, string? name, string? description, string? previewUrl, int? sortOrder, bool isActive)
    {
        Result<AdVideoVoicePresetDto> result = await _admin.UpdateVoicePresetAsync(
            id, new AdVideoVoicePresetInput(name, description ?? string.Empty, null, null, previewUrl ?? string.Empty, sortOrder, isActive));

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã lưu giọng \"{result.Value!.Name}\"."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        Result result = await _admin.DeleteVoicePresetAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã gỡ giọng khỏi danh sách chọn. Giọng vẫn còn trong tài khoản engine; video cũ không bị ảnh hưởng."
            : result.Error;

        return RedirectToPage();
    }
}
