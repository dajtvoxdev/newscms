using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị giá bán và giá khuyến mãi (gạch ngang giá gốc) của sản phẩm.
/// Tự động định dạng tiền tệ VND chuẩn xác.
/// </summary>
public sealed class ProductPriceBlock : IDynamicBlock
{
    public string Key => "product-price";

    private readonly AppDbContext _db;

    public ProductPriceBlock(AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Giá sản phẩm",
        Category: "Chi tiết",
        Description: "Hiển thị giá bán và giá so sánh/khuyến mãi của sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"23\"/><path d=\"M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6\"/></svg>",
        Presets:
        [
            new("large-bold", "Giá lớn nổi bật",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"10\" width=\"28\" height=\"12\" rx=\"2\"/><rect x=\"32\" y=\"14\" width=\"16\" height=\"6\" rx=\"1\"/></svg>",
                "{\"size\":\"lg\",\"showCompareAt\":true,\"align\":\"left\"}"),
            new("compact", "Giá gọn gàng",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"12\" width=\"22\" height=\"8\" rx=\"2\"/><rect x=\"26\" y=\"14\" width=\"14\" height=\"5\" rx=\"1\"/></svg>",
                "{\"size\":\"md\",\"showCompareAt\":true,\"align\":\"left\"}"),
            new("centered", "Căn giữa",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"10\" y=\"10\" width=\"20\" height=\"12\" rx=\"2\"/><rect x=\"32\" y=\"14\" width=\"12\" height=\"6\" rx=\"1\"/></svg>",
                "{\"size\":\"lg\",\"showCompareAt\":true,\"align\":\"center\"}")
        ],
        Props:
        [
            new("showCompareAt", BlockPropTypes.Checkbox, "Hiện giá gốc khi giảm", BlockPropGroups.Display, "true"),
            new("size", BlockPropTypes.Select, "Kích cỡ", BlockPropGroups.Display, "lg",
                Options:
                [
                    new("sm", "Nhỏ (1.1rem)"),
                    new("md", "Vừa (1.25rem)"),
                    new("lg", "Lớn (1.4rem)"),
                    new("xl", "Rất lớn (1.75rem)")
                ]),
            new("align", BlockPropTypes.Select, "Căn lề", BlockPropGroups.Display, "left",
                Options:
                [
                    new("left", "Trái"),
                    new("center", "Giữa"),
                    new("right", "Phải")
                ]),
            new("color", BlockPropTypes.Text, "Màu giá (CSS)", BlockPropGroups.Display,
                Placeholder: "vd. var(--color-ink) hoặc #0d7c66",
                Hint: "Để trống = sử dụng biến màu var(--color-ink) của theme.")
        ],
        DefaultPropsJson: "{\"showCompareAt\":true,\"size\":\"lg\",\"align\":\"left\"}",
        EntityScoped: true,
        SupportedTypes: ["product"]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var showCompareAt = props.GetBool("showCompareAt", true);
        var sizeProp = props.GetString("size") ?? "lg";
        var align = props.GetString("align") ?? "left";

        var mainSize = sizeProp switch
        {
            "sm" => "1.1rem",
            "md" => "1.25rem",
            "xl" => "1.75rem",
            _ => "1.4rem"
        };

        var compareSize = sizeProp switch
        {
            "sm" => "0.85rem",
            "md" => "0.9rem",
            "xl" => "1.1rem",
            _ => "0.95rem"
        };

        var color = BlockCss.Safe(props.GetString("color")) ?? "var(--color-ink)";
        var style = $"margin:0 0 20px;font-size:{mainSize};font-weight:700;color:{color};text-align:{align};";
        var compareStyle = $"font-size:{compareSize};font-weight:400;color:var(--color-muted);text-decoration:line-through;margin-left:10px;";

        static string Price(decimal v) => v.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div data-nc-part=\"price\" style=\"{style}\">290.000 ₫" +
                   (showCompareAt ? $" <span data-nc-part=\"price-compare\" style=\"{compareStyle}\">350.000 ₫</span>" : "") +
                   "</div>";
        }

        if (context.Route.RouteType != RouteType.Product)
            return string.Empty;

        var entityId = context.Route.EntityId;
        var p = await _db.Products.AsNoTracking()
            .Where(x => x.Id == entityId)
            .Select(x => new { x.Price, x.SalePrice })
            .FirstOrDefaultAsync(ct);

        if (p is null) return string.Empty;

        if (p.Price <= 0 && (!p.SalePrice.HasValue || p.SalePrice.Value <= 0))
        {
            return $"<div data-nc-part=\"price\" style=\"{style}\">Liên hệ</div>";
        }

        // Có giá khuyến mãi và nhỏ hơn giá gốc
        if (showCompareAt && p.SalePrice.HasValue && p.SalePrice.Value > 0 && p.SalePrice.Value < p.Price)
        {
            return $"<div data-nc-part=\"price\" style=\"{style}\">{Price(p.SalePrice.Value)}" +
                   $" <span data-nc-part=\"price-compare\" style=\"{compareStyle}\">{Price(p.Price)}</span>" +
                   "</div>";
        }

        var displayPrice = (p.SalePrice.HasValue && p.SalePrice.Value > 0) ? p.SalePrice.Value : p.Price;
        return $"<div data-nc-part=\"price\" style=\"{style}\">{Price(displayPrice)}</div>";
    }
}

