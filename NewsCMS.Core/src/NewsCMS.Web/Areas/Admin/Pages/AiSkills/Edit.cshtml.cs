using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AiSkills;

[Authorize(Permissions.Ai.ManageSkill)]
public class EditModel : PageModel
{
    private readonly IAiSkillService _service;
    private readonly IAiToolRegistry _toolRegistry;
    public EditModel(IAiSkillService service, IAiToolRegistry toolRegistry)
    {
        _service = service;
        _toolRegistry = toolRegistry;
    }

    [BindProperty]
    public AiSkillUpsertDto Input { get; set; } = default!;

    public IReadOnlyList<CreateModel.ToolOption> ToolOptions { get; private set; } = Array.Empty<CreateModel.ToolOption>();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var item = await _service.GetAllAsync();
        var skill = item.FirstOrDefault(x => x.Id == id);
        if (skill == null) return RedirectToPage("Index");

        Input = new AiSkillUpsertDto(
            skill.Id, skill.Key, skill.Name, skill.Description, skill.Kind, skill.IsActive, skill.SortOrder,
            skill.SystemPrompt, skill.UserPromptTemplate, skill.AllowStyled, skill.Temperature, skill.MaxTokens,
            skill.Targets, skill.UseTools, skill.ToolType, skill.BaseUrl, null, skill.ConfigJson);

        ToolOptions = _toolRegistry.All().Select(t => new CreateModel.ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ToolOptions = _toolRegistry.All().Select(t => new CreateModel.ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
            return Page();
        }
        var result = await _service.UpdateAsync(Input);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            ToolOptions = _toolRegistry.All().Select(t => new CreateModel.ToolOption(t.Key, t.DisplayName, t.Description)).ToList();
            return Page();
        }
        TempData["Success"] = "Đã cập nhật skill.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result.Succeeded) TempData["Error"] = result.Error;
        else TempData["Success"] = "Đã xoá skill.";
        return RedirectToPage("Index");
    }
}
