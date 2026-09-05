using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Roles;

[Authorize(Permissions.Identity.ViewRole)]
public class IndexModel : PageModel
{
    private readonly RoleManager<AppRole> _roles;
    private readonly AppDbContext _db;
    public IndexModel(RoleManager<AppRole> roles, AppDbContext db) { _roles = roles; _db = db; }

    public List<RoleRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var roles = await _roles.Roles.OrderBy(r => r.Name).ToListAsync();
        var permCounts = await _db.RolePermissions
            .GroupBy(rp => rp.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        Items = roles.Select(r => new RoleRow(
            r.Id, r.Name!, r.Description, r.IsSystem,
            permCounts.TryGetValue(r.Id, out var c) ? c : 0
        )).ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role == null) return RedirectToPage();
        if (role.IsSystem)
        {
            TempData["Error"] = "Không thể xoá role hệ thống.";
            return RedirectToPage();
        }
        await _roles.DeleteAsync(role);
        TempData["Success"] = "Đã xoá vai trò.";
        return RedirectToPage();
    }

    public record RoleRow(Guid Id, string Name, string? Description, bool IsSystem, int PermCount);
}
