using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị tình trạng kho hàng của sản phẩm (còn hàng, hết hàng, hoặc số lượng cụ thể).
/// Tự động đọc thuộc tính IsTrackingStock và StockQuantity của sản phẩm.
/// </summary>
public sealed class ProductStockBlock : IDynamicBlock
{
    public string Key => "product-stock";

    private readonly AppDbContext _db;

    public ProductStockBlock(AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Tình trạng tồn kho",
        Category: "Chi tiết",
        Description: "Hiển thị tình trạng còn hàng / hết hàng hoặc số lượng trong kho.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z\"/>" +
                 "<polyline points=\"3.27 6.96 12 12.01 20.73 6.96\"/><line x1=\"12\" y1=\"22.08\" x2=\"12\" y2=\"12\"/></svg>",
        Presets:
        [
            new("status-with-qty", "Hiện số lượng cụ thể",
                "<svg viewBox=\"0 0 48 32\"><circle cx=\"10\" cy=\"16\" r=\"4\" fill=\"#10b981\"/><rect x=\"18\" y=\"13\" width=\"26\" height=\"6\" rx=\"1.5\"/></svg>",
                "{\"showQuantity\":true,\"inStockText\":\"Còn hàng\",\"outOfStockText\":\"Hết hàng\"}"),
            new("status-only", "Chỉ hiện tình trạng",
                "<svg viewBox=\"0 0 48 32\"><circle cx=\"10\" cy=\"16\" r=\"4\" fill=\"#10b981\"/><rect x=\"18\" y=\"13\" width=\"18\" height=\"6\" rx=\"1.5\"/></svg>",
                "{\"showQuantity\":false,\"inStockText\":\"Còn hàng\",\"outOfStockText\":\"Hết hàng\"}")
        ],
        Props:
        [
            new("inStockText", BlockPropTypes.Text, "Chữ khi còn hàng", BlockPropGroups.Content, "Còn hàng",
                Placeholder: "vd. Còn hàng"),
            new("outOfStockText", BlockPropTypes.Text, "Chữ khi hết hàng", BlockPropGroups.Content, "Hết hàng",
                Placeholder: "vd. Tạm hết hàng"),
            new("showQuantity", BlockPropTypes.Checkbox, "Hiện số lượng tồn cụ thể", BlockPropGroups.Content, "true"),
            new("align", BlockPropTypes.Select, "Căn lề", BlockPropGroups.Display, "left",
                Options:
                [
                    new("left", "Trái"),
                    new("center", "Giữa"),
                    new("right", "Phải")
                ])
        ],
        DefaultPropsJson: "{\"showQuantity\":true,\"inStockText\":\"Còn hàng\",\"outOfStockText\":\"Hết hàng\",\"align\":\"left\"}",
        EntityScoped: true,
        SupportedTypes: ["product"]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var inStockText = props.GetString("inStockText") ?? "Còn hàng";
        var outOfStockText = props.GetString("outOfStockText") ?? "Hết hàng";
        var showQuantity = props.GetBool("showQuantity", true);
        var align = props.GetString("align") ?? "left";

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div style=\"margin:0 0 24px;font-weight:600;color:var(--color-brand-500,#0d7c66);text-align:{align}\">" +
                   $"{(showQuantity ? "Còn 12 sản phẩm" : WebUtility.HtmlEncode(inStockText))} (mẫu)</div>";
        }

        if (context.Route.RouteType != RouteType.Product)
            return string.Empty;

        var entityId = context.Route.EntityId;
        var p = await _db.Products.AsNoTracking()
            .Where(x => x.Id == entityId)
            .Select(x => new { x.IsTrackingStock, x.StockQuantity })
            .FirstOrDefaultAsync(ct);

        if (p is null) return string.Empty;

        // Nếu sản phẩm không quản lý tồn kho → coi như luôn còn hàng
        if (!p.IsTrackingStock)
        {
            return $"<div style=\"margin:0 0 24px;font-weight:600;color:var(--color-brand-500,#0d7c66);text-align:{align}\">" +
                   $"{WebUtility.HtmlEncode(inStockText)}</div>";
        }

        var inStock = p.StockQuantity > 0;
        var text = inStock
            ? (showQuantity ? $"Còn {p.StockQuantity:N0} sản phẩm" : inStockText)
            : outOfStockText;

        var color = inStock ? "var(--color-brand-500,#0d7c66)" : "var(--color-muted,#94a3b8)";

        return $"<div style=\"margin:0 0 24px;font-weight:600;color:{color};text-align:{align}\">" +
               $"{WebUtility.HtmlEncode(text)}</div>";
    }
}

