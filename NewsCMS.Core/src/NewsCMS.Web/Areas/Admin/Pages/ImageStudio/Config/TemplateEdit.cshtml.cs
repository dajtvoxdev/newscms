using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio.Config;

/// <summary>Thêm/sửa một mẫu prompt ảnh và ảnh demo của nó (không có id = thêm mới).</summary>
[Authorize(Roles = "SuperAdmin")]
[RequestSizeLimit(12L * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 12L * 1024 * 1024)]
public class TemplateEditModel : PageModel
{
    private readonly IImagePromptLibraryService _library;
    private readonly IImagePromptDemoService _demos;

    public TemplateEditModel(IImagePromptLibraryService library, IImagePromptDemoService demos)
    {
        _library = library;
        _demos = demos;
    }

    public ImagePromptTemplateDto? Template { get; private set; }

    public bool IsNew => Template is null;

    [BindProperty]
    public TemplateForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (id is { } templateId)
        {
            Template = await _library.GetAsync(templateId);

            if (Template is null)
            {
                return NotFound();
            }

            Input = TemplateForm.From(Template);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id)
    {
        if (id is { } existing)
        {
            Template = await _library.GetAsync(existing);

            if (Template is null)
            {
                return NotFound();
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Result<ImagePromptTemplateSaved> result = Template is null
            ? await _library.CreateAsync(Input.ToInput())
            : await _library.UpdateAsync(Template.Id, Input.ToInput());

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        TempData["Success"] = Template is null ? "Đã thêm mẫu. Tạo hoặc tải ảnh demo để người dùng thấy trước kết quả." : "Đã lưu mẫu.";

        if (result.Value!.Warnings.Count > 0)
        {
            TempData["Warning"] = string.Join(" ", result.Value.Warnings);
        }

        return RedirectToPage(new { id = result.Value.Template.Id });
    }

    public async Task<IActionResult> OnPostGenerateDemoAsync(Guid id)
    {
        Result<ImagePromptTemplateDto> result = await _demos.GenerateAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã tạo ảnh demo." : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadDemoAsync(Guid id, IFormFile? demo)
    {
        if (demo is null || demo.Length == 0)
        {
            TempData["Error"] = "Chọn một ảnh để tải lên.";
            return RedirectToPage(new { id });
        }

        await using Stream stream = demo.OpenReadStream();
        Result<ImagePromptTemplateDto> result = await _demos.UploadAsync(id, stream, demo.Length);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật ảnh demo." : result.Error;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveDemoAsync(Guid id)
    {
        Result result = await _demos.RemoveAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã xoá ảnh demo." : result.Error;
        return RedirectToPage(new { id });
    }

    public sealed class TemplateForm
    {
        [Required(ErrorMessage = "Nhập tiêu đề.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Category { get; set; } = "Khác";

        public string Purpose { get; set; } = "free";

        [StringLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Nhập prompt.")]
        [StringLength(2000)]
        public string Prompt { get; set; } = string.Empty;

        public string AspectRatio { get; set; } = "1:1";

        public int SortOrder { get; set; }

        public static TemplateForm From(ImagePromptTemplateDto t) => new()
        {
            Title = t.Title,
            Category = t.Category,
            Purpose = t.PurposeKey,
            Description = t.Description,
            Prompt = t.Prompt,
            AspectRatio = t.AspectRatio,
            SortOrder = t.SortOrder,
        };

        public ImagePromptTemplateInput ToInput() => new(
            Title, Category, ImagePurposes.Parse(Purpose) ?? ImagePurpose.Free, Description, Prompt, AspectRatio, SortOrder);
    }
}
