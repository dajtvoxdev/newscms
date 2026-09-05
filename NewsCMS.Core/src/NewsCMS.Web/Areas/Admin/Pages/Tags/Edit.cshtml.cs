using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Tags;

[Authorize(Permissions.Content.ManageTag)]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;
    public EditModel(AppDbContext db, SlugHelper slug) { _db = db; _slug = slug; }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var tag = await _db.Tags.FindAsync(Id);
        if (tag == null) return NotFound();
        Input = new InputModel { Name = tag.Name, Slug = tag.Slug };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var tag = await _db.Tags.FindAsync(Id);
        if (tag == null) return NotFound();
        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? _slug.Generate(Input.Name) : _slug.Generate(Input.Slug);
        var i = 1; var b = slug;
        while (await _db.Tags.AnyAsync(t => t.Slug == slug && t.Id != Id)) slug = $"{b}-{++i}";
        tag.Name = Input.Name.Trim();
        tag.Slug = slug;
        tag.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu tag.";
        return RedirectToPage("/Tags/Index");
    }

    public class InputModel
    {
        [Required, StringLength(100)] public string Name { get; set; } = default!;
        [StringLength(120)] public string? Slug { get; set; }
    }
}
