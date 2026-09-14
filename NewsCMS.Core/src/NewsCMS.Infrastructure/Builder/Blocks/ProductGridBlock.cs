using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block hiển thị lưới sản phẩm kèm giá. Nguồn dữ liệu qua <see cref="BlockDataFilter"/>,
/// kiểu hiển thị qua <see cref="SharedBlockProps"/>.
/// </summary>
public sealed class ProductGridBlock : IDynamicBlock, IBlockMatchCounter
{
    public string Key => "product-grid";

    private const int DefaultCount = 8;
    private const int MaxCount = 50;
    private const int MinCardWidth = 220;

    private readonly Persistence.AppDbContext _db;

    public ProductGridBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Lưới sản phẩm",
        Category: "Dữ liệu",
        Description: "Card sản phẩm kèm giá từ module bán hàng.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z\"/>" +
                 "<line x1=\"3\" y1=\"6\" x2=\"21\" y2=\"6\"/><path d=\"M16 10a4 4 0 0 1-8 0\"/></svg>",
        Presets: ProductBlockPresets.For(DefaultCount),
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            .. BlockDataFilter.ProductProps("Số sản phẩm", DefaultCount, MaxCount),
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
        query = BlockQueries.Order(query, shared, filter);
        if (shared.Skip > 0) query = query.Skip(shared.Skip);

        var products = filter.ApplyManualOrder(
            await query.Take(count)
                .Select(p => new ProductRow(
                    p.Id, p.Name, p.Slug, p.Price, p.SalePrice,
                    p.Thumbnail != null
                        ? p.Thumbnail.FilePath
                        : p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()))
                .ToListAsync(ct),
            r => r.Id);

        if (products.Count == 0) return shared.EmptyState("<!-- product-grid: no products -->");

        var sb = new StringBuilder();
        sb.Append($"<div data-nc-part=\"list\" style=\"{shared.ContainerStyle(MinCardWidth)};padding:24px 0\">");
        for (var i = 0; i < products.Count; i++) AppendCard(sb, products[i], shared, i == 0);
        sb.Append("</div>");
        return sb.ToString();
    }

    public async Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default)
        => await BlockQueries.CountProductsAsync(_db, new BlockDataFilter(new JsonProps(context.PropsJson)), ct);

    private static void AppendCard(StringBuilder sb, ProductRow p, SharedBlockProps shared, bool isFirst)
    {
        static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var imgHeight = shared.IsFeaturedLayout && isFirst ? 320 : 180;

        sb.Append($"<a data-nc-part=\"card\" href=\"/san-pham/{Enc(p.Slug)}\" style=\"text-decoration:none;color:inherit;display:block;border:1px solid #e2e8f0;border-radius:12px;padding:12px;background:#fff{shared.ItemExtraStyle(MinCardWidth, isFirst)}\">");

        if (!string.IsNullOrEmpty(p.ImageUrl))
        {
            sb.Append($"<img data-nc-part=\"image\" src=\"{Enc(p.ImageUrl)}\" alt=\"{Enc(p.Name)}\" loading=\"lazy\"");
            sb.Append($" style=\"width:100%;height:{imgHeight}px;object-fit:cover;border-radius:8px;margin-bottom:8px;display:block\">");
        }

        sb.Append($"<h4 data-nc-part=\"name\" style=\"margin:0 0 4px;font-size:.95rem;font-weight:600;color:#0f172a\">{Enc(p.Name)}</h4>");

        // Giá khuyến mại (nếu có) hiện trước, giá gốc gạch ngang bên cạnh.
        if (p.SalePrice is > 0 && p.SalePrice < p.Price)
        {
            sb.Append($"<div data-nc-part=\"price\" style=\"color:#0d7c66;font-weight:700;font-size:1.1rem\">{p.SalePrice:N0}₫");
            sb.Append($"<span data-nc-part=\"price-compare\" style=\"margin-left:8px;color:#94a3b8;font-weight:400;font-size:.85rem;text-decoration:line-through\">{p.Price:N0}₫</span></div>");
        }
        else if (p.Price > 0)
        {
            sb.Append($"<div data-nc-part=\"price\" style=\"color:#0d7c66;font-weight:700;font-size:1.1rem\">{p.Price:N0}₫</div>");
        }

        sb.Append("</a>");
    }

    private sealed record ProductRow(
        Guid Id, string Name, string Slug, decimal Price, decimal? SalePrice, string? ImageUrl);
}
