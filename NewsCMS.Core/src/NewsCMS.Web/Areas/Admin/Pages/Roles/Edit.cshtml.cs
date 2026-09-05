using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Roles;

[Authorize(Permissions.Identity.AssignPermission)]
public class EditModel : PageModel
{
    private readonly RoleManager<AppRole> _roles;
    private readonly AppDbContext _db;
    public EditModel(RoleManager<AppRole> roles, AppDbContext db) { _roles = roles; _db = db; }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string Name { get; set; } = default!;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public List<Guid> SelectedPermissions { get; set; } = new();

    public bool IsSystem { get; private set; }
    public List<PermGroup> PermGroups { get; private set; } = new();
    public HashSet<Guid> AssignedPermIds { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var role = await _roles.FindByIdAsync(Id.ToString());
        if (role == null) return NotFound();
        Name = role.Name!;
        Description = role.Description;
        IsSystem = role.IsSystem;
        await LoadPermissionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var role = await _roles.FindByIdAsync(Id.ToString());
        if (role == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(Name)) { role.Name = Name.Trim(); role.Description = Description?.Trim(); }
        await _roles.UpdateAsync(role);

        // Update permissions
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == Id).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        var allPerms = await _db.Permissions.Where(p => SelectedPermissions.Contains(p.Id)).ToListAsync();
        foreach (var perm in allPerms)
            _db.RolePermissions.Add(new RolePermission { RoleId = Id, PermissionId = perm.Id });

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu vai trò và quyền.";
        return RedirectToPage("/Roles/Index");
    }

    private async Task LoadPermissionsAsync()
    {
        var all = await _db.Permissions.OrderBy(p => p.Module).ThenBy(p => p.DisplayName).ToListAsync();
        AssignedPermIds = (await _db.RolePermissions.Where(rp => rp.RoleId == Id).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();
        PermGroups = all.GroupBy(p => p.Module)
            .Select(g => new PermGroup(g.Key, g.Select(p => new PermItem(p.Id, p.Code, p.DisplayName)).ToList()))
            .ToList();
    }

    public record PermGroup(string Module, List<PermItem> Items);
    public record PermItem(Guid Id, string Code, string DisplayName);
}
