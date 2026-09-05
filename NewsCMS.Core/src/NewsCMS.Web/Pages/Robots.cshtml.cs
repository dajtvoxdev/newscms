using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Site;

namespace NewsCMS.Web.Pages;

/// <summary>
/// robots.txt sinh theo từng site. SiteResolveMiddleware đã set ICurrentSite theo host nên mỗi
/// domain nhận đúng nội dung của site mình, kể cả khi nhiều site chạy chung một tiến trình.
///
/// Nội dung lấy từ SiteSetting key "seo.robots" (group "seo") nếu admin có tự soạn; không có thì
/// dựng bản mặc định: chặn các vùng riêng tư và trỏ tới sitemap của chính host đang gọi.
/// </summary>
[AllowAnonymous]
public class RobotsModel : PageModel
{
    /// <summary>Khoá SiteSetting cho phép admin ghi đè toàn bộ nội dung robots.txt.</summary>
    private const string RobotsSettingKey = "seo.robots";

    /// <summary>Đường dẫn không bao giờ nên để công cụ tìm kiếm bò vào.</summary>
    private static readonly string[] DisallowedPaths =
    [
        "/admin/",
        "/Account/",
        "/mcp",
        "/_nc/"
    ];

    private readonly ISiteSettingService _settings;
    private readonly ICurrentSite _currentSite;
    private readonly ISiteUrlResolver _siteUrls;

    public RobotsModel(ISiteSettingService settings, ICurrentSite currentSite, ISiteUrlResolver siteUrls)
    {
        _settings = settings;
        _currentSite = currentSite;
        _siteUrls = siteUrls;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var custom = await _settings.GetAsync<string>(RobotsSettingKey, null, ct);

        var content = string.IsNullOrWhiteSpace(custom)
            ? await BuildDefaultAsync(ct)
            : custom.Trim();

        return Content(content + "\n", "text/plain; charset=utf-8");
    }

    private async Task<string> BuildDefaultAsync(CancellationToken ct)
    {
        // Base URL lấy từ ISiteUrlResolver, KHÔNG phải Request.Host: dòng Sitemap: phải trỏ đúng
        // domain mà sitemap tự khai trong <loc> và trang tự khai ở thẻ canonical. Lấy theo host
        // đang gọi thì truy cập qua domain phụ (hoặc localhost lúc dev) sẽ chỉ crawler sang một
        // sitemap có toàn bộ URL thuộc domain khác. Resolver tự fallback về host request khi site
        // chưa khai domain nào.
        var baseUrl = await _siteUrls.GetBaseUrlAsync(ct);
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = $"{Request.Scheme}://{Request.Host.Value}";

        var lines = new List<string>
        {
            "# robots.txt sinh tự động theo site — ghi đè bằng SiteSetting 'seo.robots'.",
            $"# Site: {_currentSite.Slug}",
            string.Empty,
            "User-agent: *"
        };

        lines.AddRange(DisallowedPaths.Select(p => $"Disallow: {p}"));

        lines.Add(string.Empty);
        lines.Add($"Sitemap: {baseUrl}/sitemap.xml");

        return string.Join("\n", lines);
    }
}
