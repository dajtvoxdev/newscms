using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.AuditLogs;

[Authorize(Policy = Permissions.Audit.ViewLog)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<AuditLogRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new AuditLogRow(x.CreatedAt, x.UserName, x.Action, x.Entity, x.EntityId, x.IpAddress))
            .ToListAsync();
    }

    public record AuditLogRow(DateTime CreatedAt, string? UserName, string Action, string Entity, string? EntityId, string? IpAddress);
}
