using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>
/// Kết nối NewsCMS ↔ AdVideo và liên kết từng site với một tenant.
/// </summary>
/// <remarks>
/// SuperAdmin như các trang nền tảng khác (Sites): operator key mở quyền trên MỌI site và mọi
/// chi phí provider, không phải thứ giao cho quản trị của một site.
/// </remarks>
[Authorize(Roles = "SuperAdmin")]
public class IndexModel : PageModel
{
    private readonly IAdVideoConnectionService _connection;

    public IndexModel(IAdVideoConnectionService connection) => _connection = connection;

    public AdVideoConnectionDto Connection { get; private set; } = new(false, null, false, null, 60, null);

    public IReadOnlyList<AdVideoSiteLinkDto> Sites { get; private set; } = Array.Empty<AdVideoSiteLinkDto>();

    [BindProperty]
    public ConnectionForm Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();

        Input = new ConnectionForm
        {
            BaseUrl = Connection.BaseUrl ?? "http://127.0.0.1:5080",
            TimeoutSeconds = Connection.TimeoutSeconds,
        };
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Result result = await _connection.SaveAsync(new AdVideoConnectionInput(Input.BaseUrl, Input.OperatorKey, Input.TimeoutSeconds));

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã lưu kết nối AdVideo." : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        Result<AdVideoHealthDto> result = await _connection.TestAsync();

        if (!result.Succeeded)
        {
            return new JsonResult(new { success = false, message = result.Error });
        }

        string checks = string.Join(" · ", result.Value!.Checks.Select(c => $"{c.Key}: {c.Value}"));

        return new JsonResult(new
        {
            success = result.Value.Status == "healthy",
            message = result.Value.Status == "healthy"
                ? $"Kết nối và operator key đều đúng. {checks}"
                : $"Operator key đúng nhưng AdVideo không khoẻ: {checks}",
        });
    }

    public Task<IActionResult> OnPostLinkAsync(Guid siteId) =>
        RunAsync(() => _connection.LinkSiteAsync(siteId), "Đã kết nối site với AdVideo. Người có quyền VideoStudio của site đó dùng được ngay.");

    public Task<IActionResult> OnPostRotateAsync(Guid siteId) =>
        RunAsync(() => _connection.RotateSiteKeyAsync(siteId), "Đã cấp key mới cho site. Key cũ hết hiệu lực ngay.");

    public Task<IActionResult> OnPostUnlinkAsync(Guid siteId) =>
        RunAsync(() => _connection.UnlinkSiteAsync(siteId), "Đã bỏ kết nối và tạm ngừng tenant bên AdVideo.");

    private async Task<IActionResult> RunAsync(Func<Task<Result>> action, string success)
    {
        Result result = await action();

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Connection = await _connection.GetAsync();
        Sites = await _connection.GetSiteLinksAsync();
    }

    public sealed class ConnectionForm
    {
        public string BaseUrl { get; set; } = "";

        /// <summary>Để trống = giữ key đang có.</summary>
        public string? OperatorKey { get; set; }

        public int TimeoutSeconds { get; set; } = 60;
    }
}
