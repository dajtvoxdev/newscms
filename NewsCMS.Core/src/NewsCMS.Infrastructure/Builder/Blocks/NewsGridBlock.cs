using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Grid "Tin tức &amp; Chuyện cà phê": lấy bài viết đã publish từ module tin tức, render card giữ
/// nguyên class/biến CSS của theme (chu-card, chu-cta, var(--color-*)) nên đổi dữ liệu không đổi giao diện.
/// Props: { "categorySlug": "tin-tuc", "count": 3, "minCardWidth": 280, "ctaLabel": "Đọc tiếp", "featuredOnly": false }.
/// Ánh xạ: Title → tiêu đề, Excerpt → mô tả, Category.Name + PublishedAt → nhãn trên card,
/// FeaturedImage → ảnh, link → /{categorySlug}/{slug} (route Post sinh bởi RouteRegistry).
/// </summary>
public sealed class NewsGridBlock : IDynamicBlock, IBlockMatchCounter
{
    public string Key => "news-grid";

    private const int DefaultCount = 3;
    private const int MaxCount = 24;
    private const int ExcerptMaxChars = 180;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly Persistence.AppDbContext _db;

    public NewsGridBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Grid tin tức",
        Category: "Dữ liệu",
        Description: "Card tin tức giữ phong cách theme (chu-card) — dùng biến CSS của site.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8a2 2 0 0 0-2 2v16a2 2 0 0 1-2 2Zm0 0a2 2 0 0 1-2-2v-9c0-1.1.9-2 2-2h2\"/>" +
                 "<path d=\"M18 14h-8\"/><path d=\"M15 18h-5\"/><path d=\"M10 6h8v4h-8V6Z\"/></svg>",
        Presets: PostBlockPresets.For(DefaultCount),
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            new BlockPropDescriptor("minCardWidth", BlockPropTypes.Number, "Rộng card tối thiểu (px)",
                BlockPropGroups.Display, "280", Min: 200, Max: 480),
            .. BlockDataFilter.PostProps("Số bài hiển thị", DefaultCount, MaxCount),
            SharedBlockDescriptors.ShowExcerpt(),
            SharedBlockDescriptors.ShowDate(),
            SharedBlockDescriptors.ShowAuthor(),
            new BlockPropDescriptor("ctaLabel", BlockPropTypes.Text, "Nhãn nút đọc tiếp",
                BlockPropGroups.Content, "Đọc tiếp"),
            SharedBlockDescriptors.EmptyText()
        ],
        DefaultPropsJson: $"{{\"count\":{DefaultCount}}}");

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var filter = new BlockDataFilter(props);
        var count = Math.Clamp(props.GetInt("count", DefaultCount), 1, MaxCount);
        var minCardWidth = Math.Clamp(props.GetInt("minCardWidth", 280), 200, 480);
        var ctaLabel = props.GetString("ctaLabel") ?? "Đọc tiếp";

        var query = await BlockQueries.ApplyAsync(BlockQueries.PublishedPosts(_db), _db, filter, ct);
        query = BlockQueries.Order(query, shared, filter);
        if (shared.Skip > 0) query = query.Skip(shared.Skip);

        var posts = filter.ApplyManualOrder(
            await query
                .Take(count)
                .Select(p => new NewsRow(
                    p.Id,
                    p.Title,
                    p.Slug,
                    p.Excerpt,
                    p.PublishedAt ?? p.CreatedAt,
                    p.Category.Name,
                    p.Category.Slug,
                    p.FeaturedImage != null ? p.FeaturedImage.FilePath : null,
                    p.FeaturedImage != null ? p.FeaturedImage.AltText : null,
                    p.FeaturedImage != null ? p.FeaturedImage.Width : null,
                    p.FeaturedImage != null ? p.FeaturedImage.Height : null,
                    shared.ShowAuthor
                        ? _db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.FullName ?? u.UserName).FirstOrDefault()
                        : null))
                .ToListAsync(ct),
            r => r.Id);

        if (posts.Count == 0) return shared.EmptyState("<!-- news-grid: no posts -->");

        var sb = new StringBuilder();
        sb.Append($"<div style=\"{shared.ContainerStyle(minCardWidth)}\">");
        for (var i = 0; i < posts.Count; i++)
            AppendCard(sb, posts[i], ctaLabel, shared, minCardWidth, i == 0);
        sb.Append("</div>");
        return sb.ToString();
    }

    public async Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default)
        => await BlockQueries.CountPostsAsync(_db, new BlockDataFilter(new JsonProps(context.PropsJson)), ct);

    private static void AppendCard(
        StringBuilder sb, NewsRow post, string ctaLabel, SharedBlockProps shared, int minCardWidth, bool isFirst)
    {
        var url = $"/{post.CategorySlug}/{post.Slug}";
        // Ở layout "featured" card đầu chiếm cả hàng nên ảnh cao hơn để tương xứng.
        var imgHeight = shared.IsFeaturedLayout && isFirst ? 340 : 192;

        sb.Append($"<article class=\"chu-card\" style=\"background:var(--color-surface);border:1px solid rgba(93,46,13,0.1);border-radius:var(--radius-card);overflow:hidden;display:flex;flex-direction:column{shared.ItemExtraStyle(minCardWidth, isFirst)}\">");

        if (!string.IsNullOrWhiteSpace(post.ImageUrl))
        {
            var alt = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(post.ImageAlt) ? post.Title : post.ImageAlt);
            var size = post.Width is > 0 && post.Height is > 0
                ? $" width=\"{post.Width}\" height=\"{post.Height}\""
                : "";
            sb.Append($"<a href=\"{WebUtility.HtmlEncode(url)}\" style=\"display:block;height:{imgHeight}px;overflow:hidden;background:rgba(228,226,221,1)\">");
            sb.Append($"<img src=\"{WebUtility.HtmlEncode(post.ImageUrl)}\" alt=\"{alt}\"{size} loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover;display:block\">");
            sb.Append("</a>");
        }

        sb.Append("<div style=\"padding:20px;display:flex;flex-direction:column;flex:1\">");

        // Nhãn chuyên mục · ngày · tác giả — mỗi phần bật/tắt qua trait dùng chung.
        if (shared.ShowDate || shared.ShowAuthor)
        {
            sb.Append("<div style=\"font-size:0.7rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500);margin-bottom:8px\">");
            sb.Append(WebUtility.HtmlEncode(post.CategoryName));
            if (shared.ShowDate)
            {
                sb.Append("<span style=\"opacity:.55\"> · ");
                sb.Append(post.PublishedAt.ToString("dd.MM.yyyy", Vi));
                sb.Append("</span>");
            }
            if (shared.ShowAuthor && !string.IsNullOrWhiteSpace(post.AuthorName))
            {
                sb.Append("<span style=\"opacity:.55\"> · ");
                sb.Append(WebUtility.HtmlEncode(post.AuthorName));
                sb.Append("</span>");
            }
            sb.Append("</div>");
        }
        else
        {
            sb.Append("<div style=\"font-size:0.7rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500);margin-bottom:8px\">");
            sb.Append(WebUtility.HtmlEncode(post.CategoryName));
            sb.Append("</div>");
        }

        sb.Append("<h3 style=\"margin:0 0 10px;font-family:var(--font-display);font-size:1.1rem;color:var(--color-brand-500);line-height:1.35\">");
        sb.Append($"<a href=\"{WebUtility.HtmlEncode(url)}\" style=\"color:inherit;text-decoration:none\">");
        sb.Append(WebUtility.HtmlEncode(post.Title));
        sb.Append("</a></h3>");

        if (shared.ShowExcerpt && !string.IsNullOrWhiteSpace(post.Excerpt))
        {
            sb.Append("<p style=\"margin:0 0 14px;color:var(--color-muted);font-size:0.92rem;line-height:1.6;flex:1\">");
            sb.Append(WebUtility.HtmlEncode(Truncate(post.Excerpt, ExcerptMaxChars)));
            sb.Append("</p>");
        }

        sb.Append($"<a href=\"{WebUtility.HtmlEncode(url)}\" class=\"chu-cta\" style=\"margin-top:auto\">");
        sb.Append(WebUtility.HtmlEncode(ctaLabel));
        sb.Append(CupIconSpan);
        sb.Append(ArrowIconSpan);
        sb.Append("</a>");

        sb.Append("</div></article>");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private const string CupIconSpan =
        "<span class=\"chu-cup\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\">" +
        "<path d=\"M17 8h1a4 4 0 1 1 0 8h-1\"></path><path d=\"M3 8h14v9a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4Z\"></path>" +
        "<line x1=\"6\" y1=\"1\" x2=\"6\" y2=\"4\"></line><line x1=\"10\" y1=\"1\" x2=\"10\" y2=\"4\"></line>" +
        "<line x1=\"14\" y1=\"1\" x2=\"14\" y2=\"4\"></line></svg></span>";

    private const string ArrowIconSpan =
        "<span class=\"chu-arrow\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\">" +
        "<line x1=\"5\" y1=\"12\" x2=\"19\" y2=\"12\"></line><polyline points=\"12 5 19 12 12 19\"></polyline></svg></span>";

    private sealed record NewsRow(
        Guid Id,
        string Title,
        string Slug,
        string? Excerpt,
        DateTime PublishedAt,
        string CategoryName,
        string CategorySlug,
        string? ImageUrl,
        string? ImageAlt,
        int? Width,
        int? Height,
        string? AuthorName);
}
