using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiConnections;

[Authorize(Permissions.Ai.ManageConnection)]
public class CreateModel : PageModel
{
    private readonly IAiConnectionService _service;
    public CreateModel(IAiConnectionService service) => _service = service;

    [BindProperty]
    public AiConnectionUpsertDto Input { get; set; } = new(null, "", "", "", "", "", true, false);

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await _service.CreateAsync(Input);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return Page();
        }
        TempData["Success"] = "Đã tạo kết nối.";
        return RedirectToPage("Index");
    }
}
