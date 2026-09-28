using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>Thêm hoặc sửa một mẫu trong kho. Luật kiểm tra giống hệt mẫu AI sinh ra.</summary>
[Authorize(Roles = "SuperAdmin")]
public class TemplateEditModel : PageModel
{
    private readonly IVideoPromptLibraryService _library;

    public TemplateEditModel(IVideoPromptLibraryService library) => _library = library;

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public TemplateForm Input { get; set; } = new();

    public VideoPromptTemplateDto? Existing { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            Existing = await _library.GetAsync(id);

            if (Existing is null)
            {
                TempData["Error"] = "Mẫu không còn trong kho.";
                return RedirectToPage("/AdVideo/Templates");
            }

            Input = new TemplateForm
            {
                Title = Existing.Title,
                Category = Existing.Category,
                Description = Existing.Description,
                ScenePrompt = Existing.ScenePrompt,
                ScriptTemplate = Existing.ScriptTemplate,
                AspectRatio = Existing.AspectRatio,
                DurationSeconds = Existing.DurationSeconds,
                HasPerson = Existing.HasPerson,
                SortOrder = Existing.SortOrder,
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var input = new VideoPromptTemplateInput(
            Input.Title, Input.Category, Input.Description, Input.ScenePrompt, Input.ScriptTemplate,
            Input.AspectRatio, Input.DurationSeconds, Input.HasPerson, Input.SortOrder);

        Result<VideoPromptTemplateDto> result = Id is { } id
            ? await _library.UpdateAsync(id, input)
            : await _library.CreateAsync(input);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            Existing = Id is { } existingId ? await _library.GetAsync(existingId) : null;
            return Page();
        }

        TempData["Success"] = $"Đã lưu mẫu \"{result.Value!.Title}\".";

        return RedirectToPage("/AdVideo/Templates");
    }

    public sealed class TemplateForm
    {
        public string? Title { get; set; }
        public string? Category { get; set; } = "Khác";
        public string? Description { get; set; }
        public string? ScenePrompt { get; set; }
        public string? ScriptTemplate { get; set; }
        public string AspectRatio { get; set; } = "9:16";
        public int DurationSeconds { get; set; } = 15;
        public bool HasPerson { get; set; }
        public int SortOrder { get; set; }
    }
}
