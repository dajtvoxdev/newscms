using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Ai;

[Authorize(Permissions.Ai.UseAssist)]
public class SkillsModel : PageModel
{
    private readonly IAiSkillService _skillService;
    public SkillsModel(IAiSkillService skillService) => _skillService = skillService;

    public async Task<IActionResult> OnGetAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new JsonResult(Array.Empty<object>());

        var skills = await _skillService.GetPromptSkillsForTargetAsync(target);
        var result = skills.Select(s => new
        {
            key = s.Key,
            name = s.Name,
            // Mô tả hiển thị dưới tên skill trong modal/dropdown AI ở client.
            description = s.Description,
            allowStyled = s.AllowStyled
        });

        return new JsonResult(result);
    }
}
