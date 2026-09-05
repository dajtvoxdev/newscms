using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Render một trang builder thành HTML tài liệu đầy đủ: ghép Layout shell (hoặc shell mặc định)
/// với CompiledHtml của page tại vị trí đánh dấu data-nc-body, chèn CSS (token + site + layout + page)
/// và CustomJs (site → layout → page). CSP nonce per-request cho inline script/style.
/// </summary>
public sealed class PageRenderer : IPageRenderer
{
    /// <summary>Phần tử trong layout shell nơi nội dung page được thế vào.</summary>
    private const string BodyMarker = "data-nc-body";

    /// <summary>
    /// Bundle utility Tailwind precompile dùng chung cho mọi site Universal
    /// (nguồn: Styles/site-utilities.css, build bằng `npm run css:site`).
    /// </summary>
    private const string SiteUtilitiesCssPath = "/css/site-utilities.css";

    /// <summary>
    /// Định nghĩa các class variant <c>ncv-*</c> mà builder gán cho section.
    ///
    /// Nạp bằng link tĩnh khi trang thực sự dùng variant. Trước đây builder đọc file này qua fetch
    /// rồi nhồi toàn bộ nội dung vào Page.CustomCss mỗi lần lưu — và vì CustomCss được nạp lại vào
    /// ô soạn code ở lần mở sau, mỗi lần lưu lại cộng thêm một bản sao.
    /// </summary>
    private const string BuilderVariantCssPath = "/css/nc-builder.css";

    /// <summary>Tiền tố class variant — có mặt trong HTML thì mới cần nạp <see cref="BuilderVariantCssPath"/>.</summary>
    private const string VariantClassPrefix = "ncv-";

    private readonly AppDbContext _db;
    private readonly IDesignTokenCssBuilder _tokenCss;
    private readonly ISiteCustomCodeService _siteCode;
    private readonly DynamicBlockRenderer _dynamicBlockRenderer;
    private readonly ICurrentSite _currentSite;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITemplateResolver _templates;
    private readonly IContentTypeRegistry _contentTypes;
    private readonly ISeoMetaService _seoMeta;
    private readonly ISiteUrlResolver _siteUrls;

    // Mọi dependency đều BẮT BUỘC. Trước đây contentTypes/seoMeta là tham số tuỳ chọn có
    // fallback tự dựng bên trong — tiện cho test nhưng biến danh sách content type thành nguồn
    // sự thật thứ hai, và mỗi nơi tự dựng một registry riêng nên cache per-request không được
    // chia sẻ. Nay chỉ DI (và fixture test) khai danh sách đó.
    public PageRenderer(
        AppDbContext db,
        IDesignTokenCssBuilder tokenCss,
        ISiteCustomCodeService siteCode,
        DynamicBlockRenderer dynamicBlockRenderer,
        ICurrentSite currentSite,
        IHttpContextAccessor httpContextAccessor,
        ITemplateResolver templates,
        IContentTypeRegistry contentTypes,
        ISeoMetaService seoMeta,
        ISiteUrlResolver siteUrls)
    {
        _db = db;
        _tokenCss = tokenCss;
        _siteCode = siteCode;
        _dynamicBlockRenderer = dynamicBlockRenderer;
        _currentSite = currentSite;
        _httpContextAccessor = httpContextAccessor;
        _templates = templates;
        _contentTypes = contentTypes;
        _seoMeta = seoMeta;
        _siteUrls = siteUrls;
    }

    public Task<RenderedPage?> RenderAsync(Guid pageId, string culture, CancellationToken ct = default)
        => RenderPageAsync(pageId, culture, route: null, titleOverride: null, entityDetail: null, ct);

    /// <summary>
    /// Render một trang builder. Dùng chung cho trang thường (<paramref name="route"/> null) và
    /// cho trang TEMPLATE phục vụ một entity — khác biệt duy nhất là ngữ cảnh entity truyền xuống
    /// khối động, và <c>&lt;title&gt;</c> lấy từ entity thay vì từ tên template ("Chi tiết bài viết").
    /// </summary>
    private async Task<RenderedPage?> RenderPageAsync(
        Guid pageId, string culture, RouteContext? route, string? titleOverride,
        ContentDetail? entityDetail, CancellationToken ct)
    {
        var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, ct);

        // Chỉ render trang đã xuất bản và chưa bị xoá; ngược lại caller trả 404.
        if (page is null || page.IsDeleted) return null;
        var isPublished = page.Status == BuilderPageStatus.Published || page.IsPublished;
        if (!isPublished) return null;

        // Render dynamic blocks trong CompiledHtml của page trước khi ghép vào layout.
        var pageContent = !string.IsNullOrEmpty(page.CompiledHtml)
            ? await _dynamicBlockRenderer.RenderAsync(page.CompiledHtml, _currentSite.SiteId, culture, route, ct)
            : page.CompiledHtml;

        var title = !string.IsNullOrWhiteSpace(titleOverride)
            ? titleOverride
            : string.IsNullOrWhiteSpace(page.Title) ? _currentSite.Slug : page.Title;

        var entityType = route is not null ? route.RouteType.ToString().ToLowerInvariant() : "page";
        var entityId = route?.EntityId ?? page.Id;

        return await ComposeAsync(
            pageContent, title, culture, page.LayoutId, page.CompiledCss, page.CustomCss, page.CustomJs,
            route, entityType, entityId, entityDetail, page.Slug, ct);
    }

    public async Task<RenderedPage?> RenderEntityAsync(
        RouteType routeType, Guid entityId, string culture, string? path = null, CancellationToken ct = default)
    {
        var contentType = _contentTypes.FindByRouteType(routeType);
        if (contentType is null) return null;

        var detail = await _contentTypes.LoadDetailAsync(routeType, entityId, ct);
        if (detail is null) return null;

        var template = await _templates.ForEntityAsync(routeType, entityId, path, culture, ct);
        if (template?.PageId is { } templateId)
        {
            var rendered = await RenderPageAsync(templateId, culture, template.Route, detail.Title, detail, ct);
            if (rendered is not null) return rendered;
        }

        var fallbackHtml = await contentType.RenderFallbackHtmlAsync(detail, ct);
        if (fallbackHtml is not null)
        {
            return await ComposeAsync(
                fallbackHtml, detail.Title, culture, null, null, null, null,
                template?.Route, contentType.Key, entityId, detail, null, ct);
        }

        return null;
    }

    public Task<RenderedPage?> RenderPostAsync(
        Guid postId, string culture, string? path = null, CancellationToken ct = default) =>
        RenderEntityAsync(RouteType.Post, postId, culture, path, ct);

    public Task<RenderedPage?> RenderProductAsync(
        Guid productId, string culture, string? path = null, CancellationToken ct = default) =>
        RenderEntityAsync(RouteType.Product, productId, culture, path, ct);

    public Task<RenderedPage?> RenderCategoryAsync(
        Guid categoryId, string culture, string? path = null, CancellationToken ct = default) =>
        RenderEntityAsync(RouteType.Category, categoryId, culture, path, ct);

    public async Task<RenderedPage?> RenderTemplatePreviewAsync(
        Guid templatePageId, RouteType routeType, Guid entityId, string culture, CancellationToken ct = default)
    {
        var contentType = _contentTypes.FindByRouteType(routeType);
        if (contentType is null) return null;

        var detail = await _contentTypes.LoadDetailAsync(routeType, entityId, ct);
        if (detail is null) return null;

        var path = await contentType.BuildPathAsync(entityId, ct);
        var route = new RouteContext(routeType, entityId, detail.Slug, path, detail.CategoryId, detail.CategorySlug);

        return await RenderPageAsync(templatePageId, culture, route, detail.Title, detail, ct);
    }

    /// <summary>Ghép nội dung (page hoặc post) vào shell + CSS/JS 3 tầng và trả tài liệu hoàn chỉnh kèm SEO meta.</summary>
    /// <param name="route">
    /// Entity của URL hiện tại, để khối động TRONG SHELL (breadcrumb…) cũng có ngữ cảnh — không chỉ
    /// khối trong thân trang. Null với trang thường.
    /// </param>
    private async Task<RenderedPage> ComposeAsync(
        string? content, string title, string culture,
        Guid? layoutId, string? contentCss, string? contentCustomCss, string? contentJs,
        RouteContext? route, string? entityType, Guid? entityId, ContentDetail? detail, string? pageSlug,
        CancellationToken ct)
    {
        var layout = layoutId is { } id
            ? await _db.SiteLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted, ct)
            : await _db.SiteLayouts.AsNoTracking()
                .Where(l => l.Kind == LayoutKind.Shell && l.IsDefault && !l.IsDeleted)
                .FirstOrDefaultAsync(ct);

        // Custom code tầng site (3-tier: Site → Layout → Page).
        var siteCode = await _siteCode.GetAsync(ct);

        var tokenCssUrl = $"/_nc/site/{_currentSite.Slug}-{await _tokenCss.GetCssHashAsync(ct)}.css";

        // Reuse nonce từ CspNonceMiddleware (nếu có) để script/style inline không bị CSP chặn.
        // Middleware chạy TRƯỚC theme nên nonce đã nằm trong HttpContext.Items["__CspNonce"];
        // Dùng literal để tránh Infrastructure phụ thuộc Web (vòng tham chiếu).
        // Fallback tự sinh nếu chưa có (vd. test/render ngoài pipeline).
        var existingNonce = _httpContextAccessor.HttpContext?.Items["__CspNonce"] as string;
        var nonce = string.IsNullOrWhiteSpace(existingNonce) ? GenerateNonce() : existingNonce;

        // CSS/JS gắn riêng từng khối nằm trong attribute data-nc-css / data-nc-js của HTML.
        // Bóc ra ở đây rồi nối vào tầng tương ứng (khối của layout trước khối của page), đồng
        // thời gỡ attribute khỏi HTML gửi ra ngoài để không lộ mã nguồn và không phình trang.
        var pageBlocks = BlockCodeExtractor.Extract(content);

        // Shell cũng chạy DynamicBlockRenderer: từ khi shell dựng được bằng builder trực quan, nó
        // chứa khối động thật (site-menu cho nav header/footer). Thiếu bước này thì placeholder
        // data-nc-block trong shell ra trang public dưới dạng thẻ rỗng — menu mất hút mà không có
        // lỗi nào. Render TRƯỚC khi bóc data-nc-css/js, đúng thứ tự đang dùng cho page.
        var layoutHtml = string.IsNullOrEmpty(layout?.CompiledHtml)
            ? layout?.CompiledHtml
            : await _dynamicBlockRenderer.RenderAsync(layout.CompiledHtml, _currentSite.SiteId, culture, route, ct);
        var layoutBlocks = BlockCodeExtractor.Extract(layoutHtml);

        var needsVariantCss = UsesVariantClass(pageBlocks.Html) || UsesVariantClass(layoutBlocks.Html);

        // Phase 4: Sinh trọn bộ SEO Meta (Description, Robots, Canonical, OpenGraph, Twitter Card, Schema.org JSON-LD)
        var (seoTags, effectiveTitle) = await BuildSeoHeadTagsAsync(
            title, culture, route, entityType, entityId, detail, pageSlug, ct);

        var html = BuildHtml(
            layoutBlocks.Html, pageBlocks.Html, effectiveTitle, culture, tokenCssUrl,
            JoinParts(siteCode.CustomCss, layout?.CompiledCss, layout?.CustomCss, layoutBlocks.Css,
                      contentCss, contentCustomCss, pageBlocks.Css),
            JoinParts(siteCode.CustomJs, layout?.CustomJs, layoutBlocks.Js, contentJs, pageBlocks.Js),
            siteCode.HeadHtml, siteCode.BodyEndHtml, nonce, needsVariantCss, seoTags);

        return new RenderedPage(html, effectiveTitle, $"site:{_currentSite.SiteId}");
    }

    /// <summary>
    /// Sinh toàn bộ các thẻ SEO trong thẻ &lt;head&gt;: Title, Description, Robots, Canonical, OpenGraph,
    /// Twitter Card và Schema.org JSON-LD. Ưu tiên cấu hình thủ công trong SeoMeta; nếu chưa có
    /// thì tự động fallback thông minh từ thông tin thực thể (Post, Product, Category, Page).
    /// </summary>
    private async Task<(string SeoTags, string EffectiveTitle)> BuildSeoHeadTagsAsync(
        string title,
        string culture,
        RouteContext? route,
        string? entityType,
        Guid? entityId,
        ContentDetail? detail,
        string? pageSlug,
        CancellationToken ct)
    {
        SeoMetaDto? meta = null;
        string? jsonLd = null;

        if (!string.IsNullOrEmpty(entityType) && entityId is { } id && id != Guid.Empty)
        {
            meta = await _seoMeta.GetAsync(entityType, id, culture, ct);
            jsonLd = await _seoMeta.GenerateJsonLdAsync(entityType, id, culture, ct);
        }

        var effectiveTitle = !string.IsNullOrWhiteSpace(meta?.MetaTitle)
            ? meta.MetaTitle.Trim()
            : title;

        var metaDescription = !string.IsNullOrWhiteSpace(meta?.MetaDescription)
            ? meta.MetaDescription.Trim()
            : detail?.Excerpt;

        var robots = !string.IsNullOrWhiteSpace(meta?.Robots)
            ? meta.Robots.Trim()
            : "index, follow";

        string? canonical;
        if (!string.IsNullOrWhiteSpace(meta?.Canonical))
            canonical = meta.Canonical.Trim();
        else if (route is not null && !string.IsNullOrWhiteSpace(route.Path))
            canonical = route.Path;
        else if (pageSlug is not null)
            canonical = string.IsNullOrEmpty(pageSlug) ? "/" : $"/{pageSlug}";
        else
            canonical = null;

        string ogType;
        if (!string.IsNullOrWhiteSpace(meta?.OgType))
            ogType = meta.OgType.Trim();
        else if (entityType?.Equals("post", StringComparison.OrdinalIgnoreCase) == true)
            ogType = "article";
        else if (entityType?.Equals("product", StringComparison.OrdinalIgnoreCase) == true)
            ogType = "product";
        else
            ogType = "website";

        var ogTitle = effectiveTitle;
        var ogDescription = metaDescription;

        string? ogImage;
        if (!string.IsNullOrWhiteSpace(meta?.OgImage))
            ogImage = meta.OgImage.Trim();
        else if (!string.IsNullOrWhiteSpace(detail?.ImageUrl))
            ogImage = detail.ImageUrl.Trim();
        else
            ogImage = null;

        string twitterCard;
        if (!string.IsNullOrWhiteSpace(meta?.TwitterCard))
            twitterCard = meta.TwitterCard.Trim();
        else if (!string.IsNullOrEmpty(ogImage))
            twitterCard = "summary_large_image";
        else
            twitterCard = "summary";

        // Canonical / og:url / og:image phải là URL TUYỆT ĐỐI: Facebook, Zalo và Google bỏ qua
        // giá trị tương đối, nên "/tin-tuc/abc" tương đương không khai gì. Dựng base URL từ
        // PrimaryDomain của site — đúng cách SitemapGenerator đang làm (SitemapGenerator.cs:42).
        var baseUrl = await _siteUrls.GetBaseUrlAsync(ct);

        canonical = ToAbsoluteUrl(canonical, baseUrl);
        ogImage = ToAbsoluteUrl(ogImage, baseUrl);

        static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();

        // 1. Meta Description & Robots
        if (!string.IsNullOrWhiteSpace(metaDescription))
            sb.Append($"<meta name=\"description\" content=\"{Enc(metaDescription)}\">\n");

        sb.Append($"<meta name=\"robots\" content=\"{Enc(robots)}\">\n");

        // 2. Canonical URL
        if (!string.IsNullOrWhiteSpace(canonical))
            sb.Append($"<link rel=\"canonical\" href=\"{Enc(canonical)}\">\n");

        // 3. OpenGraph tags
        sb.Append($"<meta property=\"og:title\" content=\"{Enc(ogTitle)}\">\n");
        if (!string.IsNullOrWhiteSpace(ogDescription))
            sb.Append($"<meta property=\"og:description\" content=\"{Enc(ogDescription)}\">\n");
        sb.Append($"<meta property=\"og:type\" content=\"{Enc(ogType)}\">\n");
        if (!string.IsNullOrWhiteSpace(canonical))
            sb.Append($"<meta property=\"og:url\" content=\"{Enc(canonical)}\">\n");
        if (!string.IsNullOrWhiteSpace(ogImage))
            sb.Append($"<meta property=\"og:image\" content=\"{Enc(ogImage)}\">\n");

        // 4. Twitter Card tags
        sb.Append($"<meta name=\"twitter:card\" content=\"{Enc(twitterCard)}\">\n");
        sb.Append($"<meta name=\"twitter:title\" content=\"{Enc(ogTitle)}\">\n");
        if (!string.IsNullOrWhiteSpace(ogDescription))
            sb.Append($"<meta name=\"twitter:description\" content=\"{Enc(ogDescription)}\">\n");
        if (!string.IsNullOrWhiteSpace(ogImage))
            sb.Append($"<meta name=\"twitter:image\" content=\"{Enc(ogImage)}\">\n");

        // 5. Schema.org JSON-LD Structured Data
        if (!string.IsNullOrWhiteSpace(jsonLd))
            sb.Append($"<script type=\"application/ld+json\">\n{jsonLd}\n</script>\n");

        return (sb.ToString(), effectiveTitle);
    }

    /// <summary>
    /// Thẻ nạp font Google cho các font thật sự xuất hiện trong CSS/HTML của trang. Trả chuỗi rỗng
    /// khi trang không dùng font nào — không đánh đổi một request sang domain ngoài lấy thứ không ai
    /// cần. preconnect tới fonts.gstatic.com vì file font nằm ở đó, không nằm ở fonts.googleapis.com.
    /// </summary>
    private static string BuildGoogleFontsLink(params string?[] sources)
    {
        var url = GoogleFontCatalog.BuildStylesheetUrl(GoogleFontCatalog.DetectUsed(sources));
        if (url is null) return string.Empty;

        return "<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">\n" +
               "<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>\n" +
               $"<link rel=\"stylesheet\" href=\"{System.Net.WebUtility.HtmlEncode(url)}\">\n";
    }

    private static string BuildHtml(
        string? shellHtml, string? content, string title, string culture,
        string tokenCssUrl, string inlineCss, string customJs,
        string? headHtml, string? bodyEndHtml, string nonce, bool needsVariantCss,
        string? seoTags = null)
    {
        var nonceAttr = $"nonce=\"{nonce}\"";

        // Font Google: nạp ĐÚNG font đang được dùng, phát hiện từ chính CSS/HTML sắp render.
        // Không dùng @import trong khối <style> bên dưới vì @import chỉ hợp lệ ở đầu stylesheet,
        // mà inlineCss là nhiều tầng CSS nối lại (site → layout → page) nên font của page sẽ rơi
        // vào giữa và bị trình duyệt bỏ qua. Thẻ <link> không có ràng buộc thứ tự đó.
        var fontsLink = BuildGoogleFontsLink(inlineCss, content, shellHtml);

        // Utility precompile (npm run css:site) đứng TRƯỚC token CSS: token phải ghi đè được
        // giá trị mặc định trong bundle. Nhờ bundle này, trang tạo qua MCP/API có utility ngay
        // mà không cần ai mở builder để chạy Tailwind ở trình duyệt.
        var headCss =
            fontsLink +
            $"<link rel=\"stylesheet\" href=\"{SiteUtilitiesCssPath}\">\n" +
            $"<link rel=\"stylesheet\" href=\"{tokenCssUrl}\">\n" +
            (needsVariantCss ? $"<link rel=\"stylesheet\" href=\"{BuilderVariantCssPath}\">\n" : string.Empty) +
            (string.IsNullOrWhiteSpace(inlineCss) ? string.Empty : $"<style {nonceAttr}>{inlineCss}</style>");

        var headExtras =
            $"<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
            $"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>\n" +
            (string.IsNullOrWhiteSpace(seoTags) ? string.Empty : seoTags) +
            headCss;

        // Chèn HeadHtml (analytics/pixel snippet) vào cuối <head>.
        if (!string.IsNullOrWhiteSpace(headHtml))
            headExtras += "\n" + headHtml;

        // Script tag với nonce cho CSP.
        var jsBlock = string.IsNullOrWhiteSpace(customJs)
            ? string.Empty
            : $"<script {nonceAttr}>{customJs}</script>";

        // BodyEndHtml chèn trước </body> (sau JS của page).
        var bodyEnd = string.IsNullOrWhiteSpace(bodyEndHtml) ? string.Empty : "\n" + bodyEndHtml;

        // Không có shell riêng → dùng shell tối giản để trang vẫn chạy được ngay.
        if (string.IsNullOrWhiteSpace(shellHtml))
        {
            return
                $"<!DOCTYPE html>\n<html lang=\"{culture}\">\n<head>\n{headExtras}\n</head>\n" +
                $"<body>\n<div {BodyMarker}>{content}</div>\n{jsBlock}{bodyEnd}\n</body>\n</html>";
        }

        // Có shell: thế nội dung vào data-nc-body trước, rồi mới bọc/chèn phần <head>.
        var html = ReplaceMarkerContent(shellHtml, content);

        // ContentSanitizer loại bỏ <!DOCTYPE>/<html>/<head>/<body> — chúng không nằm trong
        // whitelist — nên CompiledHtml của layout thường chỉ còn phần thân (header/main/footer).
        // Khi đó phải tự dựng khung tài liệu, nếu không meta/title/CSS sẽ bị nối vào sau cùng
        // (ngoài <head>) và trang thiếu doctype.
        if (html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return
                $"<!DOCTYPE html>\n<html lang=\"{culture}\">\n<head>\n{headExtras}\n</head>\n" +
                $"<body>\n{html}\n{jsBlock}{bodyEnd}\n</body>\n</html>";
        }

        html = InsertBefore(html, "</head>", headExtras);
        html = InsertBefore(html, "</body>", jsBlock + bodyEnd);
        return html;
    }

    /// <summary>Thế nội dung page vào phần tử data-nc-body (giữ thẻ, chỉ thay ruột).</summary>
    private static string ReplaceMarkerContent(string html, string? content)
    {
        var markerIndex = html.IndexOf(BodyMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            // Shell không có marker → nối vào trước </body> để không mất nội dung.
            return InsertBefore(html, "</body>", content ?? string.Empty);
        }

        // Tìm '>' kết thúc thẻ mở chứa marker.
        var openTagEnd = html.IndexOf('>', markerIndex);
        if (openTagEnd < 0) return html;

        // Tìm thẻ đóng tương ứng: đơn giản là tìm '</' + tên thẻ. Lấy tên thẻ từ trước marker.
        var tagStart = html.LastIndexOf('<', markerIndex);
        if (tagStart < 0) return html;
        var tagName = ExtractTagName(html, tagStart);
        var closeTag = $"</{tagName}>";
        var closeIndex = html.IndexOf(closeTag, openTagEnd, StringComparison.OrdinalIgnoreCase);
        if (closeIndex < 0) return html;

        return string.Concat(html.AsSpan(0, openTagEnd + 1), content ?? string.Empty, html.AsSpan(closeIndex));
    }

    private static string ExtractTagName(string html, int tagStart)
    {
        var i = tagStart + 1;
        var start = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '/') i++;
        return html[start..i];
    }

    private static string InsertBefore(string html, string anchor, string insert)
    {
        var idx = html.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return html + insert;
        return string.Concat(html.AsSpan(0, idx), insert, html.AsSpan(idx));
    }

    /// <summary>Ghép nhiều phần CSS/JS theo thứ tự (bỏ qua phần rỗng).</summary>
    private static string JoinParts(params string?[] parts) =>
        string.Join("\n", parts.Where(s => !string.IsNullOrWhiteSpace(s))!);

    /// <summary>
    /// HTML có dùng class variant <c>ncv-*</c> không — quyết định nạp <see cref="BuilderVariantCssPath"/>.
    /// Kiểm ở server một lần cho mỗi lần render, thay cho việc builder nhồi file CSS vào CustomCss.
    /// </summary>
    private static bool UsesVariantClass(string? html) =>
        !string.IsNullOrEmpty(html) && html.Contains(VariantClassPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sinh nonce 16-byte base64 cho CSP per-request.</summary>
    private static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Ghép path tương đối với base URL đã tra sẵn. Giá trị đã tuyệt đối (người dùng tự khai
    /// trong SeoMeta, hoặc ảnh trên CDN) thì giữ nguyên.
    /// </summary>
    private static string? ToAbsoluteUrl(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var v = value.Trim();
        return SiteUrlResolver.IsAlreadyAbsolute(v) ? v : SiteUrlResolver.Combine(baseUrl, v);
    }
}
