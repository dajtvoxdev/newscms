using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiSkills;

[Authorize(Permissions.Ai.ManageSkill)]
public class IndexModel : PageModel
{
    private readonly IAiSkillService _service;
    public IndexModel(IAiSkillService service) => _service = service;

    public IReadOnlyList<Application.Ai.Dtos.AiSkillDto> Items { get; private set; } = Array.Empty<Application.Ai.Dtos.AiSkillDto>();

    public async Task OnGetAsync()
    {
        Items = await _service.GetAllAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá skill." : result.Error;
        return RedirectToPage();
    }
}
