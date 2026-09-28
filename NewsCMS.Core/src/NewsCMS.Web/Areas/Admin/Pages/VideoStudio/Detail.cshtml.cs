using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.VideoStudio;

/// <summary>Tiến độ, video thành phẩm và nút huỷ của một job.</summary>
[Authorize(Permissions.VideoStudio.View)]
public class DetailModel : PageModel
{
    private readonly IAdVideoClient _advideo;

    public DetailModel(IAdVideoClient advideo) => _advideo = advideo;

    public AdVideoJobDto? Job { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Result<AdVideoJobDto> result = await _advideo.GetJobAsync(id);
        Job = result.Value;
        Error = result.Error;

        return Page();
    }

    /// <summary>Trạng thái dạng JSON cho đoạn script tự làm mới — không kèm link tải (link có hạn, lấy khi tải trang).</summary>
    public async Task<IActionResult> OnGetStatusAsync(Guid id)
    {
        Result<AdVideoJobDto> result = await _advideo.GetJobAsync(id);

        return result.Succeeded
            ? new JsonResult(new
            {
                status = result.Value!.Status,
                terminal = result.Value.IsTerminal,
                progress = result.Value.ProgressPercent,
                step = result.Value.StepName,
            })
            : new JsonResult(new { error = result.Error }) { StatusCode = StatusCodes.Status502BadGateway };
    }

    [Authorize(Permissions.VideoStudio.Cancel)]
    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        Result<AdVideoJobDto> result = await _advideo.CancelJobAsync(id);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã huỷ. Bước đang chạy dở vẫn chạy nốt và vẫn tính phí."
            : result.Error;

        return RedirectToPage(new { id });
    }
}
