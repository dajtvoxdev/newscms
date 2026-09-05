using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Kiểm tra sức khỏe SEO: quét Pages, Categories, SeoMetas để tìm vấn đề.
/// </summary>
public sealed class SeoHealthService : ISeoHealthService
{
    private readonly AppDbContext _db;

    public SeoHealthService(AppDbContext db) => _db = db;

    public async Task<SeoHealthReport> CheckAsync(CancellationToken ct = default)
    {
        var issues = new List<SeoIssue>();

        // 1. Pages thiếu meta title (không có SeoMeta hoặc MetaTitle rỗng).
        // Dùng subquery Any() thay group-join: EF dịch được sang SQL EXISTS.
        var pagesWithoutMeta = await _db.Pages.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Where(p => !_db.SeoMetas.Any(m =>
                m.EntityType == "Page" && m.EntityId == p.Id && m.Culture == "vi"
                && m.MetaTitle != null && m.MetaTitle != ""))
            .Select(p => new { p.Id, p.Title })
            .ToListAsync(ct);

        foreach (var p in pagesWithoutMeta)
            issues.Add(new SeoIssue("warning", "MissingMeta", $"Trang '{p.Title}' thiếu meta title.", p.Id));

        // 2. Duplicate slugs trong cùng site.
        var dupSlugs = await _db.Pages.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.Slug)
            .Where(g => g.Count() > 1)
            .Select(g => new { Slug = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var d in dupSlugs)
            issues.Add(new SeoIssue("error", "DuplicateSlug", $"Slug '{d.Slug}' bị trùng ({d.Count} trang).", null));

        // 3. Orphan pages (đã publish nhưng chưa có SiteRoute).
        var orphanPages = await _db.Pages.AsNoTracking()
            .Where(p => !p.IsDeleted && (p.Status == Domain.Enums.BuilderPageStatus.Published || p.IsPublished))
            .Where(p => !_db.SiteRoutes.Any(r => r.RouteType == Domain.Enums.RouteType.Page && r.TargetId == p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToListAsync(ct);

        foreach (var p in orphanPages)
            issues.Add(new SeoIssue("warning", "OrphanPage", $"Trang '{p.Title}' không có route (chưa sync?).", p.Id));

        var totalPages = await _db.Pages.AsNoTracking().CountAsync(p => !p.IsDeleted, ct);

        return new SeoHealthReport(
            TotalPages: totalPages,
            PagesMissingMeta: pagesWithoutMeta.Count,
            DuplicateSlugs: dupSlugs.Count,
            OrphanPages: orphanPages.Count,
            Issues: issues.OrderByDescending(i => i.Severity == "error" ? 0 : 1).ToList());
    }
}