using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiConnections;

[Authorize(Permissions.Ai.ManageConnection)]
public class EditModel : PageModel
{
    private readonly IAiConnectionService _service;
    public EditModel(IAiConnectionService service) => _service = service;

    [BindProperty]
    public AiConnectionUpsertDto Input { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null) return RedirectToPage("Index");

        Input = new AiConnectionUpsertDto(
            item.Id, item.Name, item.Provider, item.BaseUrl,
            null, item.DefaultModel, item.IsActive, item.IsDefault,
            item.TimeoutSeconds, item.Description);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await _service.UpdateAsync(Input);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return Page();
        }
        TempData["Success"] = "Đã cập nhật kết nối.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result.Succeeded) TempData["Error"] = result.Error;
        else TempData["Success"] = "Đã xoá kết nối.";
        return RedirectToPage("Index");
    }
}
