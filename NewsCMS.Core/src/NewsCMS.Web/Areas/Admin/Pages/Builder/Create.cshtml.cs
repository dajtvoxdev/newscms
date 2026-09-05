using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

[Authorize(Permissions.Builder.PageCreate)]
public class CreateModel : PageModel
{
    private readonly IBuilderPageService _service;

    public CreateModel(IBuilderPageService service) => _service = service;

    public string? ParentTitle { get; set; }
    public string? ParentSlug { get; set; }

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? parentId = null, string? kind = null, CancellationToken ct = default)
    {
        if (parentId.HasValue && parentId.Value != Guid.Empty)
        {
            var parentRes = await _service.GetByIdAsync(parentId.Value, ct);
            if (parentRes.Succeeded && parentRes.Value is { } parent)
            {
                ParentTitle = parent.Title;
                ParentSlug = parent.Slug;
                Input.ParentPageId = parent.Id;
                Input.Kind = !string.IsNullOrWhiteSpace(kind) ? kind : "PostTemplate";
                Input.Title = $"Chi tiết {parent.Title}";
                Input.Slug = string.IsNullOrWhiteSpace(parent.Slug) ? "chi-tiet" : $"{parent.Slug}-chi-tiet";
            }
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var result = await _service.CreateAsync(new BuilderPageSaveRequest(
            Title: Input.Title,
            Slug: Input.Slug ?? string.Empty,
            BuilderJson: null,
            CompiledHtml: null,
            CompiledCss: null,
            CustomCss: null,
            CustomJs: null,
            Kind: Input.Kind,
            ParentPageId: Input.ParentPageId,
            IsDefaultTemplate: Input.IsDefaultTemplate
        ), ct);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return Page();
        }

        // Redirect thẳng vào builder editor cho trang vừa tạo.
        return RedirectToPage("/Builder/Edit", new { id = result.Value!.Id });
    }

    public sealed class CreateInput
    {
        [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
        [MaxLength(300)]
        public string Title { get; set; } = string.Empty;

        [RegularExpression("^[a-z0-9\\-]*$", ErrorMessage = "Slug chỉ được chứa chữ thường, số và dấu gạch ngang.")]
        [MaxLength(320)]
        public string? Slug { get; set; }

        public string Kind { get; set; } = "Landing";

        public Guid? ParentPageId { get; set; }

        public bool IsDefaultTemplate { get; set; }
    }
}
