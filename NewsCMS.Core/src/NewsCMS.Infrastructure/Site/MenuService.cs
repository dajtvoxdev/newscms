using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Site;

public class MenuService : IMenuService
{
    private readonly AppDbContext _db;
    public MenuService(AppDbContext db) => _db = db;

    public async Task<MenuTreeDto?> GetByLocationAsync(string location, CancellationToken ct = default)
    {
        var menu = await _db.Menus
            .AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Location == location, ct);

        if (menu == null) return null;

        var allItems = menu.Items.ToList();
        var rootItems = allItems
            .Where(i => i.ParentId == null)
            .OrderBy(i => i.Order)
            .Select(i => BuildItem(i, allItems))
            .ToList();

        return new MenuTreeDto(menu.Id, menu.Name, menu.Location, rootItems);
    }

    private static MenuItemDto BuildItem(
        Domain.Entities.Site.MenuItem item,
        List<Domain.Entities.Site.MenuItem> all)
    {
        var children = all
            .Where(c => c.ParentId == item.Id)
            .OrderBy(c => c.Order)
            .Select(c => BuildItem(c, all))
            .ToList();

        return new MenuItemDto(item.Id, item.Title, item.Url, item.Target, item.Icon, children);
    }
}
