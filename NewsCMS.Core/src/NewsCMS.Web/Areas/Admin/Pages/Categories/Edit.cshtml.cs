using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Categories;

[Authorize(Permissions.Content.ManageCategory)]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;

    public EditModel(AppDbContext db, SlugHelper slug)
    {
        _db = db;
        _slug = slug;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> ParentOptions { get; private set; } = new();
    public List<SelectListItem> TemplateOptions { get; private set; } = new();
    public List<SelectListItem> LayoutOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var cat = await _db.Categories.FindAsync(Id);
        if (cat == null) return NotFound();

        Input = new InputModel
        {
            Name = cat.Name,
            Slug = cat.Slug,
            Description = cat.Description,
            ParentId = cat.ParentId,
            Order = cat.Order,
            IsActive = cat.IsActive,
            TemplatePageId = cat.TemplatePageId,
            LayoutId = cat.LayoutId
        };
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();
        if (!ModelState.IsValid) return Page();

        var cat = await _db.Categories.FindAsync(Id);
        if (cat == null) return NotFound();

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? _slug.Generate(Input.Name) : _slug.Generate(Input.Slug);
        var i = 1; var baseSlug = slug;
        while (await _db.Categories.AnyAsync(c => c.Slug == slug && c.Id != Id)) slug = $"{baseSlug}-{++i}";

        cat.Name = Input.Name.Trim();
        cat.Slug = slug;
        cat.Description = Input.Description?.Trim();
        cat.ParentId = Id == Input.ParentId ? null : Input.ParentId; // prevent self-parent
        cat.Order = Input.Order;
        cat.IsActive = Input.IsActive;
        cat.TemplatePageId = Input.TemplatePageId == Guid.Empty ? null : Input.TemplatePageId;
        cat.LayoutId = Input.LayoutId == Guid.Empty ? null : Input.LayoutId;
        cat.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu chuyên mục.";
        return RedirectToPage("/Categories/Index");
    }

    private async Task LoadOptionsAsync()
    {
        ParentOptions = await _db.Categories
            .Where(c => c.ParentId == null && c.Id != Id)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();

        TemplateOptions = await _db.Pages.AsNoTracking()
            .Where(p => p.Kind == PageKind.CategoryTemplate || p.Kind == PageKind.PostTemplate || p.Kind == PageKind.Static)
            .OrderBy(p => p.Title)
            .Select(p => new SelectListItem($"{p.Title} ({p.Kind})", p.Id.ToString()))
            .ToListAsync();

        LayoutOptions = await _db.SiteLayouts.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync();
    }

    public class InputModel
    {
        [Required, StringLength(200)] public string Name { get; set; } = default!;
        [StringLength(220)] public string? Slug { get; set; }
        [StringLength(500)] public string? Description { get; set; }
        public Guid? ParentId { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? TemplatePageId { get; set; }
        public Guid? LayoutId { get; set; }
    }
}
