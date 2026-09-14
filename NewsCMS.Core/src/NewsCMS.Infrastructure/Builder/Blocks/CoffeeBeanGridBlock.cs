using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Grid "hạt cà phê đặc trưng": lấy sản phẩm đã publish theo chuyên mục, render card giữ nguyên
/// class/biến CSS của theme (chu-card, chu-tag, var(--color-*)) nên đổi dữ liệu không đổi giao diện.
/// Props: { "categorySlug": "hat-ca-phe", "count": 6, "featuredOnly": false }.
/// Ánh xạ: Name → tiêu đề, ShortDescription → chip nguồn gốc, Description → hướng dẫn pha (rich text),
/// Thumbnail hoặc ảnh đầu tiên → ảnh card, SortOrder → thứ tự. Card bọc trong link /san-pham/{slug}.
/// </summary>
public sealed class CoffeeBeanGridBlock : IDynamicBlock, IBlockMatchCounter
{
    public string Key => "coffee-bean-grid";

    private const int DefaultCount = 6;
    private const int MaxCount = 24;
    private const int MinCardWidth = 280;

    private readonly Persistence.AppDbContext _db;

    public CoffeeBeanGridBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Grid sản phẩm đặc trưng",
        Category: "Dữ liệu",
        Description: "Card sản phẩm kèm chip nguồn gốc và hướng dẫn pha (chu-card).",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10Z\"/>" +
                 "<path d=\"M2 21c0-3 1.85-5.36 5.08-6C9.5 14.52 12 13 13 12\"/></svg>",
        Presets: ProductBlockPresets.For(DefaultCount),
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            .. BlockDataFilter.ProductProps("Số sản phẩm", DefaultCount, MaxCount),
            SharedBlockDescriptors.ShowExcerpt(),
            SharedBlockDescriptors.EmptyText()
        ],
        DefaultPropsJson: $"{{\"count\":{DefaultCount}}}");

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var filter = new BlockDataFilter(props);
        var count = Math.Clamp(props.GetInt("count", DefaultCount), 1, MaxCount);

        var query = await BlockQueries.ApplyAsync(BlockQueries.PublishedProducts(_db), _db, filter, ct);
        // Grid đặc trưng vốn xếp theo SortOrder người dùng sắp — giữ mặc định đó khi chưa chọn gì.
        query = BlockQueries.Order(query, shared, filter, fallbackOrder: "manual");
        if (shared.Skip > 0) query = query.Skip(shared.Skip);

        var beans = filter.ApplyManualOrder(
            await query
                .Take(count)
                .Select(p => new BeanRow(
                    p.Id,
                    p.Name,
                    p.Slug,
                    p.ShortDescription,
                    p.Description,
                    p.Thumbnail != null ? p.Thumbnail.FilePath : p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                    p.Thumbnail != null ? p.Thumbnail.AltText : p.Images.OrderBy(i => i.SortOrder).Select(i => i.AltText).FirstOrDefault(),
                    p.Thumbnail != null ? p.Thumbnail.Width : null,
                    p.Thumbnail != null ? p.Thumbnail.Height : null))
                .ToListAsync(ct),
            r => r.Id);

        if (beans.Count == 0) return shared.EmptyState("<!-- coffee-bean-grid: no beans -->");

        var sb = new StringBuilder();
        sb.Append($"<div data-nc-part=\"list\" style=\"{shared.ContainerStyle(MinCardWidth, 24)}\">");
        for (var i = 0; i < beans.Count; i++) AppendCard(sb, beans[i], shared, i == 0);
        sb.Append("</div>");
        return sb.ToString();
    }

    public async Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default)
        => await BlockQueries.CountProductsAsync(_db, new BlockDataFilter(new JsonProps(context.PropsJson)), ct);

    private static void AppendCard(StringBuilder sb, BeanRow bean, SharedBlockProps shared, bool isFirst)
    {
        sb.Append("<a data-nc-part=\"card\" class=\"chu-card\" href=\"/san-pham/");
        sb.Append(WebUtility.HtmlEncode(bean.Slug));
        sb.Append($"\" style=\"background:var(--color-subtle);padding:28px;display:flex;flex-direction:column;border-radius:var(--radius-card);text-decoration:none;color:inherit{shared.ItemExtraStyle(MinCardWidth, isFirst)}\">");

        if (!string.IsNullOrWhiteSpace(bean.ImageUrl))
        {
            var alt = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(bean.ImageAlt) ? bean.Name : bean.ImageAlt);
            var size = bean.Width is > 0 && bean.Height is > 0
                ? $" width=\"{bean.Width}\" height=\"{bean.Height}\""
                : "";
            var imgHeight = shared.IsFeaturedLayout && isFirst ? 340 : 200;
            sb.Append($"<div data-nc-part=\"image\" style=\"height:{imgHeight}px;overflow:hidden;background:rgba(219,218,213,1);border-radius:var(--radius-card);margin-bottom:20px\">");
            sb.Append($"<img src=\"{WebUtility.HtmlEncode(bean.ImageUrl)}\" alt=\"{alt}\"{size} loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover\">");
            sb.Append("</div>");
        }

        sb.Append("<h3 data-nc-part=\"title\" style=\"margin:0 0 10px;font-family:var(--font-display);font-size:1.35rem;color:var(--color-brand-500)\">");
        sb.Append(WebUtility.HtmlEncode(bean.Name));
        sb.Append("</h3>");

        if (!string.IsNullOrWhiteSpace(bean.Origin))
        {
            sb.Append("<div data-nc-part=\"origin\" style=\"margin-bottom:18px\"><span class=\"chu-tag\" style=\"background:rgba(228,226,221,1);color:var(--color-ink)\">");
            sb.Append(LeafIconSvg);
            sb.Append(WebUtility.HtmlEncode(bean.Origin));
            sb.Append("</span></div>");
        }

        if (shared.ShowExcerpt && !string.IsNullOrWhiteSpace(bean.Description))
        {
            sb.Append("<div data-nc-part=\"excerpt\" style=\"flex:1;font-size:.9rem;color:var(--color-muted);line-height:1.6\">");
            // Description đã đi qua ContentSanitizer khi lưu ở ProductService.
            sb.Append(bean.Description);
            sb.Append("</div>");
        }

        sb.Append("</a>");
    }

    private const string LeafIconSvg =
        "<svg viewBox=\"0 0 24 24\"><path d=\"M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10Z\"/>" +
        "<path d=\"M2 21c0-3 1.85-5.36 5.08-6C9.5 14.52 12 13 13 12\"/></svg>";

    private sealed record BeanRow(
        Guid Id,
        string Name,
        string Slug,
        string? Origin,
        string? Description,
        string? ImageUrl,
        string? ImageAlt,
        int? Width,
        int? Height);
}
