using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>
/// Kho mẫu brief cho form tạo video: danh sách, duyệt/ẩn/xoá, cấu hình tự cập nhật theo trend, lịch sử chạy.
/// </summary>
/// <remarks>
/// Kho nằm ở NewsCMS (không phải AdVideo): mẫu chỉ là thứ điền sẵn vào form, và việc sinh mẫu dùng
/// kết nối AI + công cụ tìm web NewsCMS đã có.
/// </remarks>
[Authorize(Roles = "SuperAdmin")]
public class TemplatesModel : PageModel
{
    private readonly IVideoPromptLibraryService _library;
    private readonly IVideoPromptTrendService _trends;

    public TemplatesModel(IVideoPromptLibraryService library, IVideoPromptTrendService trends)
    {
        _library = library;
        _trends = trends;
    }

    public IReadOnlyList<VideoPromptTemplateDto> Templates { get; private set; } = Array.Empty<VideoPromptTemplateDto>();
    public IReadOnlyList<VideoPromptTrendRunDto> Runs { get; private set; } = Array.Empty<VideoPromptTrendRunDto>();
    public int PendingCount { get; private set; }

    [BindProperty]
    public VideoPromptLibrarySettingsDto Settings { get; set; } = new(false, 24, 6, 14, false, null);

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await _library.GetSettingsAsync();
        Templates = await _library.GetAllAsync(Source, Status);
        Runs = await _library.GetRunsAsync(8);
        PendingCount = (await _library.GetAllAsync(status: "pending_review")).Count;
    }

    public async Task<IActionResult> OnPostSettingsAsync()
    {
        Result result = await _library.SaveSettingsAsync(Settings);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? Settings.TrendAutoUpdateEnabled
                ? $"Đã lưu. Kho tự cập nhật theo trend mỗi {Settings.TrendIntervalHours} giờ (lần đầu trong vòng 15 phút nếu chưa chạy bao giờ)."
                : "Đã lưu. Tự cập nhật theo trend đang tắt — vẫn bấm \"Cập nhật ngay\" được."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        Result<VideoPromptTrendRunDto> result = await _trends.RefreshAsync(User.Identity?.Name ?? "quản trị");

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã thêm {result.Value!.Added} mẫu theo trend, loại {result.Value.Rejected} mẫu không đạt, ẩn {result.Value.Expired} mẫu hết hạn."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, string newStatus)
    {
        Result result = await _library.SetStatusAsync(id, newStatus);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? newStatus switch
            {
                "published" => "Mẫu đã hiện trong form tạo video.",
                "hidden" => "Đã ẩn mẫu.",
                _ => "Đã đổi trạng thái.",
            }
            : result.Error;

        return RedirectToPage(new { Source, Status });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        Result result = await _library.DeleteAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá mẫu khỏi kho." : result.Error;

        return RedirectToPage(new { Source, Status });
    }
}
