using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block hiển thị danh sách bài viết. Nguồn dữ liệu qua <see cref="BlockDataFilter"/>
/// (nhiều chuyên mục / tag / chọn tay / loại trừ), kiểu hiển thị qua <see cref="SharedBlockProps"/>.
/// Render CSS inline nên không phụ thuộc stylesheet ngoài — theme nào cũng dùng được.
/// </summary>
public sealed class PostListBlock : IDynamicBlock, IBlockMatchCounter
{
    public string Key => "post-list";

    private const int DefaultCount = 6;
    private const int MaxCount = 50;
    private const int MinCardWidth = 280;

    private readonly Persistence.AppDbContext _db;

    public PostListBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Danh sách bài viết",
        Category: "Dữ liệu",
        Description: "Card bài viết theo chuyên mục, thẻ hoặc danh sách chọn tay.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"3\" y=\"4\" width=\"18\" height=\"18\" rx=\"2\"/><line x1=\"3\" y1=\"10\" x2=\"21\" y2=\"10\"/>" +
                 "<line x1=\"9\" y1=\"4\" x2=\"9\" y2=\"20\"/></svg>",
        Presets: PostBlockPresets.For(DefaultCount),
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            .. BlockDataFilter.PostProps("Số bài hiển thị", DefaultCount, MaxCount),
            SharedBlockDescriptors.ShowExcerpt(),
            SharedBlockDescriptors.ShowDate(),
            SharedBlockDescriptors.EmptyText()
        ],
        DefaultPropsJson: $"{{\"count\":{DefaultCount}}}");

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var filter = new BlockDataFilter(props);
        var count = Math.Clamp(props.GetInt("count", DefaultCount), 1, MaxCount);

        var query = await BlockQueries.ApplyAsync(BlockQueries.PublishedPosts(_db), _db, filter, ct);
        query = BlockQueries.Order(query, shared, filter);
        if (shared.Skip > 0) query = query.Skip(shared.Skip);

        var posts = filter.ApplyManualOrder(
            await query.Take(count)
                .Select(p => new PostRow(
                    p.Id, p.Title, p.Slug, p.Excerpt,
                    p.PublishedAt ?? p.CreatedAt,
                    p.FeaturedImage != null ? p.FeaturedImage.FilePath : null,
                    p.FeaturedImage != null ? p.FeaturedImage.Width : null,
                    p.FeaturedImage != null ? p.FeaturedImage.Height : null,
                    p.Category.Slug))
                .ToListAsync(ct),
            r => r.Id);

        if (posts.Count == 0) return shared.EmptyState("<!-- post-list: no posts -->");

        var sb = new StringBuilder();
        sb.Append($"<div data-nc-part=\"list\" style=\"{shared.ContainerStyle(MinCardWidth, 24)};padding:24px 0\">");
        for (var i = 0; i < posts.Count; i++) AppendCard(sb, posts[i], shared, i == 0);
        sb.Append("</div>");
        return sb.ToString();
    }

    public async Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default)
        => await BlockQueries.CountPostsAsync(_db, new BlockDataFilter(new JsonProps(context.PropsJson)), ct);

    private static void AppendCard(StringBuilder sb, PostRow p, SharedBlockProps shared, bool isFirst)
    {
        static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var url = $"/{Enc(p.CategorySlug)}/{Enc(p.Slug)}";
        // Mục nổi bật ở layout "featured" được ảnh cao hơn để thực sự nổi lên.
        var big = shared.IsFeaturedLayout && isFirst;
        var imgHeight = big ? 320 : 180;

        sb.Append($"<a data-nc-part=\"card\" href=\"{url}\" style=\"text-decoration:none;color:inherit;display:block{shared.ItemExtraStyle(MinCardWidth, isFirst)}\">");
        sb.Append("<div data-nc-part=\"card-body\" style=\"border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;padding:16px;background:#fff\">");

        if (!string.IsNullOrEmpty(p.ImageUrl))
        {
            var size = p.Width is > 0 && p.Height is > 0 ? $" width=\"{p.Width}\" height=\"{p.Height}\"" : "";
            sb.Append($"<img data-nc-part=\"image\" src=\"{Enc(p.ImageUrl)}\" alt=\"{Enc(p.Title)}\"{size} loading=\"lazy\"");
            sb.Append($" style=\"width:100%;height:{imgHeight}px;object-fit:cover;border-radius:8px;margin-bottom:12px;display:block\">");
        }

        if (shared.ShowDate)
        {
            sb.Append("<div data-nc-part=\"date\" style=\"font-size:.75rem;color:#94a3b8;margin-bottom:6px\">");
            sb.Append(p.PublishedAt.ToString("dd.MM.yyyy"));
            sb.Append("</div>");
        }

        sb.Append($"<h3 data-nc-part=\"title\" style=\"margin:0 0 8px;font-size:{(big ? "1.4rem" : "1.1rem")};font-weight:700;color:#0f172a\">");
        sb.Append(Enc(p.Title));
        sb.Append("</h3>");

        if (shared.ShowExcerpt && !string.IsNullOrEmpty(p.Excerpt))
        {
            sb.Append("<p data-nc-part=\"excerpt\" style=\"margin:0;color:#64748b;font-size:.9rem;line-height:1.5\">");
            sb.Append(Enc(Truncate(p.Excerpt, big ? 220 : 120)));
            sb.Append("</p>");
        }

        sb.Append("</div></a>");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private sealed record PostRow(
        Guid Id, string Title, string Slug, string? Excerpt, DateTime PublishedAt,
        string? ImageUrl, int? Width, int? Height, string CategorySlug);
}
