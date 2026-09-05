using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ProductCategories;

[Authorize(Permissions.Catalog.ManageCategory)]
public class CreateModel : PageModel
{
    private readonly IProductCategoryService _svc;
    public CreateModel(IProductCategoryService svc) => _svc = svc;

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Parents { get; private set; } = new();

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? ParentId { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadParentsAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadParentsAsync(ct);
        if (!ModelState.IsValid) return Page();

        var dto = new ProductCategoryUpsertDto(null, Input.Name, Input.Slug ?? string.Empty, Input.Description, Input.Order, Input.IsActive, Input.ParentId);
        var result = await _svc.CreateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Lỗi không xác định.");
            return Page();
        }

        TempData["Success"] = $"Đã tạo chuyên mục \"{Input.Name}\".";
        return RedirectToPage("/ProductCategories/Index");
    }

    private async Task LoadParentsAsync(CancellationToken ct)
    {
        var items = await _svc.GetAllAsync(ct);
        Parents = items.Where(x => x.ParentId == null)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
    }
}
