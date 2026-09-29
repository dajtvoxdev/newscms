using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio.Config;

/// <summary>Danh sách model tạo ảnh. SuperAdmin như Cấu hình AdVideo: model dùng key và tiền của cả nền tảng.</summary>
[Authorize(Roles = "SuperAdmin")]
public class ModelsModel : PageModel
{
    private readonly IImageModelService _models;

    public ModelsModel(IImageModelService models) => _models = models;

    public IReadOnlyList<ImageModelDto> Models { get; private set; } = Array.Empty<ImageModelDto>();

    public async Task OnGetAsync() => Models = await _models.GetAllAsync();

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        Result result = await _models.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá model." : result.Error;
        return RedirectToPage();
    }

    /// <summary>"Chạy thử" gọi bằng fetch — tốn tiền thật một ảnh chất lượng thấp nhất của model.</summary>
    public async Task<IActionResult> OnPostTestAsync(Guid id)
    {
        Result<ImageModelTestResultDto> result = await _models.TestAsync(id);

        if (!result.Succeeded)
        {
            return new JsonResult(new { ok = false, error = result.Error });
        }

        ImageModelTestResultDto test = result.Value!;

        return new JsonResult(new { ok = test.Ok, previewUrl = test.PreviewUrl, error = test.Error, seconds = Math.Round(test.DurationMs / 1000.0, 1) });
    }
}
