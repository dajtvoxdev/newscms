using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiConnections;

[Authorize(Permissions.Ai.ManageConnection)]
public class IndexModel : PageModel
{
    private readonly IAiConnectionService _service;
    public IndexModel(IAiConnectionService service) => _service = service;

    public IReadOnlyList<Application.Ai.Dtos.AiConnectionDto> Items { get; private set; } = Array.Empty<Application.Ai.Dtos.AiConnectionDto>();

    public async Task OnGetAsync()
    {
        Items = await _service.GetAllAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá kết nối." : result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetDefaultAsync(Guid id)
    {
        var result = await _service.SetDefaultAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã đặt mặc định." : result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid id)
    {
        var result = await _service.TestAsync(id);
        return new JsonResult(new { success = result.Succeeded, message = result.Succeeded ? "Kết nối thành công!" : result.Error });
    }
}
