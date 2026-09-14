using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị danh sách bài viết liên quan trong trang chi tiết bài viết.
/// Tự động loại trừ bài viết đang xem (excludeIds = [current]) và ưu tiên lấy bài cùng chuyên mục.
/// </summary>
public sealed class RelatedPostsBlock : IDynamicBlock
{
    public string Key => "related-posts";

    private const int DefaultCount = 4;
    private const int MaxCount = 12;
    private const int MinCardWidth = 260;

    private readonly AppDbContext _db;

    public RelatedPostsBlock(AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Bài viết liên quan",
        Category: "Chi tiết",
        Description: "Danh sách bài viết cùng chuyên mục, tự động loại trừ bài đang xem.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2\"/>" +
                 "<rect x=\"8\" y=\"2\" width=\"8\" height=\"4\" rx=\"1\"/><path d=\"m9 14 2 2 4-4\"/></svg>",
        Presets:
        [
            new("grid-4", "Lưới 4 cột",
                SharedBlockDescriptors.GridThumb(4),
                "{\"layout\":\"grid\",\"columns\":4,\"count\":4,\"sameCategory\":true}"),
            new("grid-3", "Lưới 3 cột",
                SharedBlockDescriptors.GridThumb(3),
                "{\"layout\":\"grid\",\"columns\":3,\"count\":3,\"sameCategory\":true}"),
            new("list-compact", "Danh sách dọc gọn gàng",
                SharedBlockDescriptors.ListThumb,
                "{\"layout\":\"list\",\"columns\":0,\"count\":4,\"sameCategory\":true}")
        ],
        Props:
        [
            new("title", BlockPropTypes.Text, "Tiêu đề khối", BlockPropGroups.Content, "Bài viết liên quan",
                Placeholder: "vd. Bài viết liên quan hoặc Có thể bạn quan tâm"),
            new("sameCategory", BlockPropTypes.Checkbox, "Chỉ lấy bài cùng chuyên mục", BlockPropGroups.Data, "true"),
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            SharedBlockDescriptors.Count("Số bài hiển thị", DefaultCount, MaxCount),
            SharedBlockDescriptors.ShowExcerpt(),
            SharedBlockDescriptors.ShowDate(),
            SharedBlockDescriptors.EmptyText()
        ],
        DefaultPropsJson: "{\"title\":\"Bài viết liên quan\",\"count\":4,\"sameCategory\":true,\"layout\":\"grid\",\"columns\":4}",
        EntityScoped: true,
        SupportedTypes: ["post"]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var title = props.GetString("title") ?? "Bài viết liên quan";
        var sameCategory = props.GetBool("sameCategory", true);
        var count = Math.Clamp(props.GetInt("count", DefaultCount), 1, MaxCount);

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<section data-nc-part=\"wrapper\" style=\"margin-top:48px;padding-top:32px;border-top:1px solid #e2e8f0;\">" +
                   $"<h3 data-nc-part=\"title\" style=\"margin:0 0 20px;font-size:1.3rem;font-weight:600;color:var(--color-brand-500,#0f172a)\">{WebUtility.HtmlEncode(title)} (mẫu)</h3>" +
                   $"<div data-nc-part=\"list\" style=\"{shared.ContainerStyle(MinCardWidth, 20)}\">" +
                   "<div data-nc-part=\"card\" style=\"border:1px dashed #cbd5e1;border-radius:12px;padding:16px;background:#f8fafc;color:#94a3b8;font-size:0.9rem\">Bài viết liên quan 1</div>" +
                   "<div data-nc-part=\"card\" style=\"border:1px dashed #cbd5e1;border-radius:12px;padding:16px;background:#f8fafc;color:#94a3b8;font-size:0.9rem\">Bài viết liên quan 2</div>" +
                   "</div></section>";
        }

        var currentPostId = context.Route.EntityId;
        var query = BlockQueries.PublishedPosts(_db).Where(p => p.Id != currentPostId);

        if (sameCategory)
        {
            Guid? categoryId = context.Route.CategoryId;
            if (!categoryId.HasValue && context.Route.RouteType == RouteType.Post)
            {
                categoryId = await _db.Posts.AsNoTracking()
                    .Where(p => p.Id == currentPostId)
                    .Select(p => (Guid?)p.CategoryId)
                    .FirstOrDefaultAsync(ct);
            }

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }
        }

        query = query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt);

        var posts = await query.Take(count)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.Excerpt,
                PublishedAt = p.PublishedAt ?? p.CreatedAt,
                ImageUrl = p.FeaturedImage != null ? p.FeaturedImage.FilePath : null,
                CategorySlug = p.Category != null ? p.Category.Slug : "tin-tuc"
            })
            .ToListAsync(ct);

        if (posts.Count == 0)
            return shared.EmptyState("<!-- related-posts: no matching posts -->");

        var sb = new StringBuilder();
        sb.Append("<section data-nc-part=\"wrapper\" style=\"margin-top:48px;padding-top:32px;border-top:1px solid var(--color-subtle, #e2e8f0);\">");

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append($"<h3 data-nc-part=\"title\" style=\"margin:0 0 24px;font-family:var(--font-display);font-size:1.35rem;font-weight:600;color:var(--color-brand-500,#0f172a)\">");
            sb.Append(WebUtility.HtmlEncode(title));
            sb.Append("</h3>");
        }

        sb.Append($"<div data-nc-part=\"list\" style=\"{shared.ContainerStyle(MinCardWidth, 20)}\">");
        for (var i = 0; i < posts.Count; i++)
        {
            var p = posts[i];
            var url = $"/{WebUtility.HtmlEncode(p.CategorySlug)}/{WebUtility.HtmlEncode(p.Slug)}";
            var encTitle = WebUtility.HtmlEncode(p.Title);

            sb.Append($"<a data-nc-part=\"card\" href=\"{url}\" style=\"text-decoration:none;color:inherit;display:block{shared.ItemExtraStyle(MinCardWidth, i == 0)}\">");
            sb.Append("<div data-nc-part=\"card-body\" style=\"border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;padding:14px;background:#fff;height:100%;box-sizing:border-box\">");

            if (!string.IsNullOrEmpty(p.ImageUrl))
            {
                sb.Append($"<img data-nc-part=\"image\" src=\"{WebUtility.HtmlEncode(p.ImageUrl)}\" alt=\"{encTitle}\" loading=\"lazy\" ");
                sb.Append("style=\"width:100%;height:160px;object-fit:cover;border-radius:8px;margin-bottom:12px;display:block\">");
            }

            if (shared.ShowDate)
            {
                sb.Append("<div data-nc-part=\"date\" style=\"font-size:.75rem;color:#94a3b8;margin-bottom:6px\">");
                sb.Append(p.PublishedAt.ToString("dd.MM.yyyy"));
                sb.Append("</div>");
            }

            sb.Append($"<h4 data-nc-part=\"item-title\" style=\"margin:0 0 6px;font-size:1rem;font-weight:600;line-height:1.4;color:#0f172a\">{encTitle}</h4>");

            if (shared.ShowExcerpt && !string.IsNullOrEmpty(p.Excerpt))
            {
                sb.Append($"<p data-nc-part=\"excerpt\" style=\"margin:0;font-size:.85rem;color:#64748b;line-height:1.5;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden\">");
                sb.Append(WebUtility.HtmlEncode(p.Excerpt));
                sb.Append("</p>");
            }

            sb.Append("</div></a>");
        }

        sb.Append("</div></section>");
        return sb.ToString();
    }
}

