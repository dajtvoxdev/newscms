using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Menus;

[Authorize(Policy = Permissions.Site.ManageMenu)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        _db.Menus.Add(new Menu { Name = Input.Name.Trim(), Location = Input.Location.Trim() });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo menu.";
        return RedirectToPage("/Menus/Index");
    }

    public class InputModel
    {
        [Required, StringLength(100)] public string Name { get; set; } = default!;
        [Required, StringLength(50)]  public string Location { get; set; } = "header";
    }
}
