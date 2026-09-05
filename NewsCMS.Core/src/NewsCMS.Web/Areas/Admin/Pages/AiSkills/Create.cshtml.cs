using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiSkills;

[Authorize(Permissions.Ai.ManageSkill)]
public class CreateModel : PageModel
{
    private readonly IAiSkillService _service;
    private readonly IAiToolRegistry _toolRegistry;
    public CreateModel(IAiSkillService service, IAiToolRegistry toolRegistry)
    {
        _service = service;
        _toolRegistry = toolRegistry;
    }

    [BindProperty]
    public AiSkillUpsertDto Input { get; set; } = new(null, "", "", null, AiSkillKind.Prompt, true);

    public IReadOnlyList<ToolOption> ToolOptions { get; private set; } = Array.Empty<ToolOption>();

    public void OnGet()
    {
        ToolOptions = _toolRegistry.All().Select(t => new ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ToolOptions = _toolRegistry.All().Select(t => new ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
            return Page();
        }
        var result = await _service.CreateAsync(Input);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            ToolOptions = _toolRegistry.All().Select(t => new ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
            return Page();
        }
        TempData["Success"] = "Đã tạo skill.";
        return RedirectToPage("Index");
    }

    public record ToolOption(string Key, string DisplayName, string Description);
}
