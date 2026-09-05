using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ProductCategories;

[Authorize(Permissions.Catalog.ManageCategory)]
public class EditModel : PageModel
{
    private readonly IProductCategoryService _svc;
    private readonly AppDbContext _db;

    public EditModel(IProductCategoryService svc, AppDbContext db)
    {
        _svc = svc;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Parents { get; private set; } = new();
    public List<SelectListItem> TemplateOptions { get; private set; } = new();

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? ParentId { get; set; }
        public Guid? TemplatePageId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var item = await _svc.GetByIdAsync(Id, ct);
        if (item is null) return NotFound();

        Input = new InputModel
        {
            Name = item.Name,
            Slug = item.Slug,
            Description = item.Description,
            Order = item.Order,
            IsActive = item.IsActive,
            ParentId = item.ParentId,
            TemplatePageId = item.TemplatePageId
        };
        await LoadOptionsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadOptionsAsync(ct);
        if (!ModelState.IsValid) return Page();

        var templatePageId = Input.TemplatePageId == Guid.Empty ? null : Input.TemplatePageId;
        var dto = new ProductCategoryUpsertDto(Id, Input.Name, Input.Slug, Input.Description, Input.Order, Input.IsActive, Input.ParentId, templatePageId);
        var result = await _svc.UpdateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Lỗi không xác định.");
            return Page();
        }
        TempData["Success"] = "Đã cập nhật chuyên mục.";
        return RedirectToPage("/ProductCategories/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var result = await _svc.DeleteAsync(Id, ct);
        if (!result.Succeeded) TempData["Error"] = result.Error;
        else TempData["Success"] = "Đã xoá chuyên mục.";
        return RedirectToPage("/ProductCategories/Index");
    }

    private async Task LoadOptionsAsync(CancellationToken ct)
    {
        var items = await _svc.GetAllAsync(ct);
        Parents = items.Where(x => x.Id != Id && x.ParentId == null)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();

        TemplateOptions = await _db.Pages.AsNoTracking()
            .Where(p => p.Kind == PageKind.ProductTemplate || p.Kind == PageKind.CategoryTemplate || p.Kind == PageKind.Static)
            .OrderBy(p => p.Title)
            .Select(p => new SelectListItem($"{p.Title} ({p.Kind})", p.Id.ToString()))
            .ToListAsync(ct);
    }
}
