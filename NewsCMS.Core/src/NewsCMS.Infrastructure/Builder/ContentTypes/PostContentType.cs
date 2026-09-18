using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.ContentTypes;

/// <summary>
/// Triển khai IContentType cho loại nội dung "Bài viết" (Post).
/// Đọc dữ liệu từ bảng Posts, chuẩn hoá thành ContentDetail và hỗ trợ HTML fallback.
/// </summary>
public sealed class PostContentType : IContentType
{
    public string Key => "post";
    public string DisplayName => "Bài viết";
    public RouteType RouteType => RouteType.Post;
    public PageKind TemplateKind => PageKind.PostTemplate;

    private readonly AppDbContext _db;

    public PostContentType(AppDbContext db) => _db = db;

    public async Task<ContentDetail?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.AsNoTracking()
            .Where(p => p.Id == id && !p.IsDeleted && p.Status == PostStatus.Published)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Title,
                p.Content,
                p.Excerpt,
                PublishedAt = p.PublishedAt ?? p.CreatedAt,
                p.CategoryId,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                CategoryName = p.Category != null ? p.Category.Name : null,
                ImageUrl = p.FeaturedImage != null ? p.FeaturedImage.FilePath : null,
                ImageAlt = p.FeaturedImage != null ? p.FeaturedImage.AltText : null,
                p.ViewCount
            })
            .FirstOrDefaultAsync(ct);

        if (post is null) return null;

        var extra = new Dictionary<string, object?>
        {
            ["ViewCount"] = post.ViewCount
        };

        return new ContentDetail(
            Id: post.Id,
            Slug: post.Slug,
            Title: post.Title,
            Body: post.Content,
            Excerpt: post.Excerpt,
            ImageUrl: post.ImageUrl,
            ImageWidth: null,
            ImageHeight: null,
            ImageAlt: post.ImageAlt,
            PublishedAt: post.PublishedAt,
            CategoryId: post.CategoryId,
            CategorySlug: post.CategorySlug,
            CategoryName: post.CategoryName,
            Extra: extra);
    }

    public async Task<string> BuildPathAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Slug,
                CategorySlug = p.Category != null ? p.Category.Slug : "tin-tuc",
                CategoryPathSlug = p.Category != null ? p.Category.PathSlug : null
            })
            .FirstOrDefaultAsync(ct);

        if (post is null) return "/";

        var categorySegment = string.IsNullOrWhiteSpace(post.CategoryPathSlug)
            ? post.CategorySlug
            : post.CategoryPathSlug;

        return IRouteRegistry.NormalizePath($"/{categorySegment}/{post.Slug}");
    }

    /// <summary>
    /// Fallback legacy (trước đây là BuildPostHtml trong PageRenderer): sinh HTML mặc định cho bài viết
    /// khi site chưa thiết lập trang Builder Template tương ứng.
    /// Giữ nguyên mã nguồn để tương thích ngược 100% với các site legacy hoặc môi trường test tối giản.
    /// </summary>
    public Task<string?> RenderFallbackHtmlAsync(ContentDetail detail, CancellationToken ct = default)
    {
        // Fallback legacy C# StringBuilder nối chuỗi HTML
        static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();

        sb.Append("<article style=\"padding:var(--space-section,72px) 24px;max-width:820px;margin:0 auto\">");

        if (!string.IsNullOrWhiteSpace(detail.CategorySlug))
        {
            sb.Append($"<a href=\"/{Enc(detail.CategorySlug)}\" style=\"font-size:0.72rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500);text-decoration:none\">");
            sb.Append(Enc(detail.CategoryName));
            sb.Append("</a>");
        }

        sb.Append("<h1 style=\"margin:12px 0 10px;font-family:var(--font-display);font-weight:400;font-size:clamp(1.8rem,4vw,2.6rem);line-height:1.25;color:var(--color-brand-500)\">");
        sb.Append(Enc(detail.Title));
        sb.Append("</h1>");

        if (detail.PublishedAt.HasValue)
        {
            sb.Append("<div style=\"color:var(--color-muted);font-size:0.85rem;margin-bottom:28px\">");
            sb.Append(detail.PublishedAt.Value.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("vi-VN")));
            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(detail.ImageUrl))
        {
            var alt = Enc(string.IsNullOrWhiteSpace(detail.ImageAlt) ? detail.Title : detail.ImageAlt);
            sb.Append("<div style=\"border-radius:var(--radius-card,18px);overflow:hidden;margin-bottom:36px;background:rgba(228,226,221,1)\">");
            sb.Append($"<img src=\"{Enc(detail.ImageUrl)}\" alt=\"{alt}\" style=\"width:100%;height:auto;display:block\">");
            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(detail.Excerpt))
        {
            sb.Append("<p style=\"font-size:1.15rem;line-height:1.6;color:var(--color-ink);margin:0 0 28px;opacity:.9\">");
            sb.Append(Enc(detail.Excerpt));
            sb.Append("</p>");
        }

        sb.Append("<div class=\"nc-post-body\" style=\"font-size:1.05rem;line-height:1.8;color:var(--color-ink)\">");
        sb.Append(detail.Body ?? string.Empty);
        sb.Append("</div>");

        // Fallback này là đường render DUY NHẤT cho site Universal chưa có PostTemplate
        // (vd chu-kafe): EntityContentBlock chỉ chạy khi template tồn tại. Video/audio chèn
        // từ TinyMCE chỉ được full-width + skin Plyr nếu nhúng ở đây — cùng chuỗi byte với
        // EntityContentBlock qua PlyrAssets.ForBody để mọi đường render lệch nhau không tái diễn
        // bug "trang chi tiết không thấy Plyr". Script inline được PageRenderer.StampInlineScriptNonce
        // gắn nonce CSP per-request khi ráp trang.
        sb.Append(PlyrAssets.ForBody(detail.Body));

        sb.Append("</article>");
        return Task.FromResult<string?>(sb.ToString());
    }
}

