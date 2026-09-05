using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Menus;

[Authorize(Policy = Permissions.Site.ManageMenu)]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    public EditModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string MenuName { get; set; } = default!;
    [BindProperty] public string MenuLocation { get; set; } = default!;
    [BindProperty] public ItemInput NewItem { get; set; } = new();

    public List<MenuItem> Items { get; private set; } = new();
    public List<SelectListItem> ParentOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var menu = await _db.Menus.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == Id);
        if (menu == null) return NotFound();

        MenuName = menu.Name;
        MenuLocation = menu.Location;
        Items = menu.Items.OrderBy(i => i.Order).ToList();
        ParentOptions = Items.Select(i => new SelectListItem(i.Title, i.Id.ToString())).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAddItemAsync()
    {
        var menu = await _db.Menus.FirstOrDefaultAsync(m => m.Id == Id);
        if (menu == null) return NotFound();

        if (string.IsNullOrWhiteSpace(NewItem.Title) || string.IsNullOrWhiteSpace(NewItem.Url))
        {
            TempData["Error"] = "Tiêu đề và URL không được trống.";
            return RedirectToPage(new { Id });
        }

        _db.MenuItems.Add(new MenuItem
        {
            MenuId   = Id,
            Title    = NewItem.Title.Trim(),
            Url      = NewItem.Url.Trim(),
            Target   = NewItem.Target,
            Icon     = NewItem.Icon?.Trim(),
            Order    = NewItem.Order,
            ParentId = NewItem.ParentId
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm mục menu.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostDeleteItemAsync(Guid itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item != null)
        {
            _db.MenuItems.Remove(item);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xoá mục menu.";
        }
        return RedirectToPage(new { Id });
    }

    public class ItemInput
    {
        [StringLength(100)] public string Title { get; set; } = default!;
        [StringLength(300)] public string Url   { get; set; } = default!;
        public string? Target   { get; set; }
        public string? Icon     { get; set; }
        public int     Order    { get; set; }
        public Guid?   ParentId { get; set; }
    }
}
