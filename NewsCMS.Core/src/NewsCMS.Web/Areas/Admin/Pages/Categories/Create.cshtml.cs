using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Categories;

[Authorize(Permissions.Content.ManageCategory)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;

    public CreateModel(AppDbContext db, SlugHelper slug)
    {
        _db = db;
        _slug = slug;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> ParentOptions { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadParentsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadParentsAsync();
        if (!ModelState.IsValid) return Page();

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? _slug.Generate(Input.Name) : _slug.Generate(Input.Slug);
        var i = 1;
        var baseSlug = slug;
        while (await _db.Categories.AnyAsync(c => c.Slug == slug)) slug = $"{baseSlug}-{++i}";

        _db.Categories.Add(new Category
        {
            Name = Input.Name.Trim(),
            Slug = slug,
            Description = Input.Description?.Trim(),
            ParentId = Input.ParentId,
            Order = Input.Order,
            IsActive = Input.IsActive
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo chuyên mục.";
        return RedirectToPage("/Categories/Index");
    }

    private async Task LoadParentsAsync()
    {
        ParentOptions = await _db.Categories
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
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
    }
}
