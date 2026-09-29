using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio.Config;

/// <summary>
/// Kho mẫu prompt ảnh: cấu hình tự cập nhật theo trend, lịch sử chạy, ảnh demo, danh sách mẫu (duyệt/ẩn/xoá).
/// </summary>
/// <remarks>SuperAdmin như các trang cấu hình nền tảng: mẫu dùng chung mọi site, và mỗi lần tạo demo tốn tiền.</remarks>
[Authorize(Roles = "SuperAdmin")]
public class TemplatesModel : PageModel
{
    /// <summary>Trần một lần bấm "Tạo demo cho mọi mẫu chưa có" — giữ request HTTP trong vài phút.</summary>
    public const int DemoBatchSize = 12;

    private readonly IImagePromptLibraryService _library;
    private readonly IImagePromptTrendService _trends;
    private readonly IImagePromptDemoService _demos;
    private readonly IImageModelService _models;

    public TemplatesModel(IImagePromptLibraryService library, IImagePromptTrendService trends, IImagePromptDemoService demos, IImageModelService models)
    {
        _library = library;
        _trends = trends;
        _demos = demos;
        _models = models;
    }

    public IReadOnlyList<ImagePromptTemplateDto> Templates { get; private set; } = Array.Empty<ImagePromptTemplateDto>();

    public IReadOnlyList<ImagePromptTrendRunDto> Runs { get; private set; } = Array.Empty<ImagePromptTrendRunDto>();

    public IReadOnlyList<ImageModelDto> Models { get; private set; } = Array.Empty<ImageModelDto>();

    public ImageDemoEstimateDto DemoEstimate { get; private set; } = new(0, 0, null, null);

    public int PendingCount { get; private set; }

    [BindProperty]
    public ImagePromptLibrarySettingsDto Settings { get; set; } = new(false, 24, 6, 14, false, null, null, false, 6, null);

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Purpose { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await _library.GetSettingsAsync();
        IReadOnlyList<ImagePromptTemplateDto> all = await _library.GetAllAsync(Source, Status);
        Templates = ImagePurposes.Parse(Purpose) is { } p ? all.Where(t => t.Purpose == p).ToList() : all;
        Runs = await _library.GetRunsAsync(8);
        PendingCount = (await _library.GetAllAsync(status: "pending_review")).Count;
        Models = (await _models.GetAllAsync()).Where(m => m.IsActive).ToList();
        DemoEstimate = await _demos.EstimateMissingAsync();
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
        Result<ImagePromptTrendRunDto> result = await _trends.RefreshAsync(User.Identity?.Name ?? "quản trị");

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã thêm {result.Value!.Added} mẫu theo trend, loại {result.Value.Rejected} mẫu không đạt, ẩn {result.Value.Expired} mẫu hết hạn."
              + (result.Value.Added > 0 ? " Bấm \"Tạo demo\" để có ảnh minh hoạ cho mẫu mới." : string.Empty)
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, string newStatus)
    {
        Result result = await _library.SetStatusAsync(id, newStatus);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? newStatus switch
            {
                "published" => "Mẫu đã hiện trong modal tạo ảnh.",
                "hidden" => "Đã ẩn mẫu.",
                _ => "Đã đổi trạng thái.",
            }
            : result.Error;

        return RedirectToPage(new { Source, Status, Purpose });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        Result result = await _library.DeleteAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá mẫu khỏi kho." : result.Error;

        return RedirectToPage(new { Source, Status, Purpose });
    }

    public async Task<IActionResult> OnPostDemoAsync(Guid id)
    {
        Result<ImagePromptTemplateDto> result = await _demos.GenerateAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Đã tạo ảnh demo cho \"{result.Value!.Title}\"." : result.Error;

        return RedirectToPage(new { Source, Status, Purpose });
    }

    public async Task<IActionResult> OnPostDemoMissingAsync()
    {
        Result<ImageDemoBatchResultDto> result = await _demos.GenerateMissingAsync(DemoBatchSize);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
        }
        else if (result.Value!.Failed == 0)
        {
            TempData["Success"] = $"Đã tạo {result.Value.Created} ảnh demo.";
        }
        else
        {
            TempData["Warning"] = $"Tạo được {result.Value.Created} ảnh demo, lỗi {result.Value.Failed}: {result.Value.Notes}";
        }

        return RedirectToPage(new { Source, Status, Purpose });
    }
}
