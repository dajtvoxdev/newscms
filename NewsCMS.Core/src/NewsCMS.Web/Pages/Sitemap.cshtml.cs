using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Pages;

/// <summary>
/// Sitemap cho mọi theme. Site Universal → sinh từ SiteRoute (qua ISitemapGenerator, kèm hreflang);
/// theme RCL cũ (HaiLuuNguoc/KeoBia2026/PhuPhucYaka) → giữ nguyên nhánh legacy để không đổi hành vi.
/// </summary>
[AllowAnonymous]
public class SitemapModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite? _currentSite;
    private readonly ISitemapGenerator? _sitemapGenerator;

    public SitemapModel(AppDbContext db, ICurrentSite? currentSite = null, ISitemapGenerator? sitemapGenerator = null)
    {
        _db = db;
        _currentSite = currentSite;
        _sitemapGenerator = sitemapGenerator;
    }

    public List<SitemapUrl> Urls { get; private set; } = new();

    /// <summary>Không null khi site dùng theme Universal — view sẽ xuất XML từ generator.</summary>
    public string? UniversalXml { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // Theme Universal: sitemap sinh từ SiteRoute (kèm hreflang, priority từ SeoMeta).
        if (string.Equals(_currentSite?.Theme, "Universal", StringComparison.OrdinalIgnoreCase)
            && _sitemapGenerator is not null)
        {
            UniversalXml = await _sitemapGenerator.GenerateAsync(ct);
            Response.ContentType = "application/xml; charset=utf-8";
            return Content(UniversalXml, "application/xml");
        }

        // ── Legacy: theme RCL cũ — giữ nguyên hành vi ─────────────────────
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // Home + theme static routes
        Urls.Add(new SitemapUrl($"{baseUrl}/", DateTime.UtcNow, "daily", 1.0));
        if (IsHaiLuuNguoc)
        {
            Urls.Add(new SitemapUrl($"{baseUrl}/cam-ket", DateTime.UtcNow, "weekly", 0.8));
            Urls.Add(new SitemapUrl($"{baseUrl}/ar", DateTime.UtcNow, "weekly", 0.6));
        }
        if (HasProducts)
            Urls.Add(new SitemapUrl($"{baseUrl}/san-pham", DateTime.UtcNow, "weekly", 0.7));

        // Categories
        var cats = await _db.Categories.Where(c => c.IsActive)
            .Select(c => new { c.Slug, c.UpdatedAt, c.CreatedAt }).ToListAsync(ct);
        foreach (var c in cats)
            Urls.Add(new SitemapUrl($"{baseUrl}/{c.Slug}", c.UpdatedAt ?? c.CreatedAt, "weekly", 0.7));

        // Posts (published)
        var posts = await _db.Posts.Where(p => p.Status == PostStatus.Published)
            .Select(p => new { CategorySlug = p.Category.Slug, PostSlug = p.Slug, p.UpdatedAt, p.PublishedAt }).ToListAsync(ct);
        foreach (var p in posts)
            Urls.Add(new SitemapUrl($"{baseUrl}/{p.CategorySlug}/{p.PostSlug}",
                p.UpdatedAt ?? p.PublishedAt ?? DateTime.UtcNow, "monthly", 0.8));

        // Static pages
        var pages = await _db.Pages.Where(p => p.IsPublished)
            .Select(p => new { p.Slug, p.UpdatedAt, p.CreatedAt }).ToListAsync(ct);
        foreach (var pg in pages)
            Urls.Add(new SitemapUrl($"{baseUrl}/page/{pg.Slug}", pg.UpdatedAt ?? pg.CreatedAt, "monthly", 0.5));

        if (HasProducts)
        {
            var products = await _db.Products.Where(p => p.Status == ProductStatus.Published)
                .Select(p => new { p.Slug, p.UpdatedAt, p.PublishedAt }).ToListAsync(ct);
            foreach (var p in products)
                Urls.Add(new SitemapUrl($"{baseUrl}/san-pham/{p.Slug}", p.UpdatedAt ?? p.PublishedAt ?? DateTime.UtcNow, "monthly", 0.7));
        }

        if (IsHaiLuuNguoc)
        {
            var arExperiences = await _db.ArExperiences.Where(x => x.IsPublished)
                .Select(x => new { x.Slug, x.UpdatedAt, x.CreatedAt }).ToListAsync(ct);
            foreach (var ar in arExperiences)
                Urls.Add(new SitemapUrl($"{baseUrl}/ar/{ar.Slug}", ar.UpdatedAt ?? ar.CreatedAt, "monthly", 0.5));
        }

        Response.ContentType = "application/xml; charset=utf-8";
        return Page();
    }

    private bool IsHaiLuuNguoc => string.Equals(_currentSite?.Theme, "HaiLuuNguoc", StringComparison.OrdinalIgnoreCase);

    private bool HasProducts => _currentSite is null
        || IsHaiLuuNguoc
        || string.Equals(_currentSite.Theme, "PhuPhucYaka", StringComparison.OrdinalIgnoreCase);

    public record SitemapUrl(string Loc, DateTime LastMod, string ChangeFreq, double Priority);
}
