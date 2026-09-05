using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Users;

[Authorize(Permissions.Identity.ViewUser)]
public class IndexModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    public IndexModel(UserManager<AppUser> users, AppDbContext db) { _users = users; _db = db; }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Active { get; set; }

    public List<UserRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var q = _users.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Keyword))
            q = q.Where(u => u.Email!.Contains(Keyword) || (u.FullName != null && u.FullName.Contains(Keyword)));
        if (Active.HasValue)
            q = q.Where(u => u.IsActive == Active.Value);

        var users = await q.OrderByDescending(u => u.CreatedAt).Take(100).ToListAsync();
        var userRoles = new Dictionary<Guid, string>();
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            userRoles[u.Id] = string.Join(", ", roles);
        }
        Items = users.Select(u => new UserRow(u.Id, u.Email!, u.FullName, userRoles[u.Id], u.IsActive, u.CreatedAt, u.LastLoginAt)).ToList();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user != null)
        {
            user.IsActive = !user.IsActive;
            await _users.UpdateAsync(user);
            TempData["Success"] = user.IsActive ? "Đã mở khoá người dùng." : "Đã khoá người dùng.";
        }
        return RedirectToPage(new { q = Keyword, Active });
    }

    public record UserRow(Guid Id, string Email, string? FullName, string Roles, bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt);
}
