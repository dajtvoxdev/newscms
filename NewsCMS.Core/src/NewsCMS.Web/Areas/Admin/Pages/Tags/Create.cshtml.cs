using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Tags;

[Authorize(Permissions.Content.ManageTag)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;
    public CreateModel(AppDbContext db, SlugHelper slug) { _db = db; _slug = slug; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? _slug.Generate(Input.Name) : _slug.Generate(Input.Slug);
        var i = 1; var b = slug;
        while (await _db.Tags.AnyAsync(t => t.Slug == slug)) slug = $"{b}-{++i}";
        _db.Tags.Add(new Tag { Name = Input.Name.Trim(), Slug = slug });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo tag.";
        return RedirectToPage("/Tags/Index");
    }

    public class InputModel
    {
        [Required, StringLength(100)] public string Name { get; set; } = default!;
        [StringLength(120)] public string? Slug { get; set; }
    }
}
