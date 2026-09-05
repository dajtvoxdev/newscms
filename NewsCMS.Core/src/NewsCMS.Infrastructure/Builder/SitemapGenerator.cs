using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Sinh sitemap XML từ SiteRoute của site hiện tại. Mỗi URL kèm lastmod, priority, changefreq
/// từ SeoMeta nếu có. hreflang sinh từ tập route cùng TargetId khác Culture.
/// </summary>
public sealed class SitemapGenerator : ISitemapGenerator
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;
    private readonly ISiteUrlResolver _siteUrls;

    public SitemapGenerator(AppDbContext db, ICurrentSite currentSite, ISiteUrlResolver siteUrls)
    {
        _db = db;
        _currentSite = currentSite;
        _siteUrls = siteUrls;
    }

    public async Task<string> GenerateAsync(CancellationToken ct = default)
    {
        var routes = await _db.SiteRoutes.AsNoTracking()
            .Where(r => r.IsPrimary)
            .OrderBy(r => r.Path)
            .ToListAsync(ct);

        // Lấy SeoMeta cho tất cả entity trong một query để tránh N+1.
        var entityIds = routes.Where(r => r.TargetId.HasValue).Select(r => r.TargetId!.Value).Distinct().ToList();
        var seoMetas = entityIds.Count > 0
            ? await _db.SeoMetas.AsNoTracking()
                .Where(m => entityIds.Contains(m.EntityId))
                .ToDictionaryAsync(m => (m.EntityType, m.EntityId, m.Culture), m => m, ct)
            : new Dictionary<(string, Guid, string), Domain.Entities.Seo.SeoMeta>();

        // Base URL dùng CHUNG với thẻ canonical/og:url (ISiteUrlResolver), thay cho việc chỉ đọc
        // Site.PrimaryDomain rồi fallback ra chuỗi "localhost": site đã khai domain thật trong
        // bảng SiteDomains mà bỏ trống PrimaryDomain sẽ phát sitemap trỏ https://localhost/... —
        // Google không dùng được gì, và lệch hẳn với canonical trên chính trang đó.
        var baseUrl = await _siteUrls.GetBaseUrlAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
        sb.AppendLine("        xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");

        foreach (var route in routes)
        {
            var url = baseUrl + route.Path;
            var seo = route.TargetId.HasValue
                ? seoMetas.GetValueOrDefault((route.RouteType.ToString(), route.TargetId.Value, route.Culture))
                : null;

            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{System.Net.WebUtility.HtmlEncode(url)}</loc>");

            if (seo is not null)
            {
                if (seo.Priority.HasValue)
                    sb.AppendLine($"    <priority>{seo.Priority.Value:F1}</priority>");
                if (!string.IsNullOrEmpty(seo.ChangeFreq))
                    sb.AppendLine($"    <changefreq>{seo.ChangeFreq}</changefreq>");
            }

            // hreflang: tìm các route khác cùng TargetId nhưng khác Culture.
            if (route.TargetId.HasValue)
            {
                var alternates = routes.Where(r => r.TargetId == route.TargetId && r.Culture != route.Culture).ToList();
                foreach (var alt in alternates)
                {
                    var altUrl = baseUrl + alt.Path;
                    sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{alt.Culture}\" href=\"{System.Net.WebUtility.HtmlEncode(altUrl)}\" />");
                }
                // Self hreflang.
                sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{route.Culture}\" href=\"{System.Net.WebUtility.HtmlEncode(url)}\" />");
            }

            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }

    public async Task<string> GenerateRobotsTxtAsync(CancellationToken ct = default)
    {
        // Cùng nguồn base URL với sitemap và thẻ canonical — dòng Sitemap: trong robots.txt phải
        // là URL tuyệt đối, và phải trỏ đúng domain mà sitemap tự khai trong <loc>.
        var baseUrl = await _siteUrls.GetBaseUrlAsync(ct);
        if (string.IsNullOrEmpty(baseUrl)) baseUrl = "https://localhost";

        var sb = new StringBuilder();
        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /");
        sb.AppendLine("");
        sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");
        return sb.ToString();
    }
}
