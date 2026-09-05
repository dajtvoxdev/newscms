using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị ảnh đại diện tiêu biểu của bài viết hoặc ảnh chính của sản phẩm.
/// Tự động lấy ảnh từ FeaturedImage (bài viết) hoặc Thumbnail/Images (sản phẩm).
/// </summary>
public sealed class EntityImageBlock : IDynamicBlock
{
    public string Key => "entity-image";

    private readonly IContentTypeRegistry _contentTypes;

    public EntityImageBlock(IContentTypeRegistry contentTypes) => _contentTypes = contentTypes;

    public BlockDescriptor Descriptor => new(
        Label: "Ảnh đại diện chi tiết",
        Category: "Chi tiết",
        Description: "Ảnh tiêu biểu của bài viết hoặc ảnh chính của sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.5\"/>" +
                 "<polyline points=\"21 15 16 10 5 21\"/></svg>",
        Presets:
        [
            new("auto-card", "Ảnh gốc bo góc thẻ",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"2\" y=\"2\" width=\"44\" height=\"28\" rx=\"6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/></svg>",
                "{\"ratio\":\"auto\",\"radius\":\"card\",\"fit\":\"cover\"}"),
            new("16-9-cover", "Tỉ lệ 16:9 cắt tràn",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"4\" width=\"48\" height=\"24\" rx=\"3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/></svg>",
                "{\"ratio\":\"16:9\",\"radius\":\"md\",\"fit\":\"cover\"}"),
            new("square", "Vuông 1:1",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"10\" y=\"2\" width=\"28\" height=\"28\" rx=\"4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/></svg>",
                "{\"ratio\":\"1:1\",\"radius\":\"lg\",\"fit\":\"cover\"}")
        ],
        Props:
        [
            new("ratio", BlockPropTypes.Select, "Tỉ lệ khung hình", BlockPropGroups.Display, "auto",
                Options:
                [
                    new("auto", "Tự động theo tỉ lệ gốc"),
                    new("16:9", "16:9 (video/bài viết rộng)"),
                    new("4:3", "4:3 (chuẩn ảnh ngang)"),
                    new("1:1", "1:1 (vuông sản phẩm)"),
                    new("21:9", "21:9 (panorama/banner rộng)")
                ]),
            new("radius", BlockPropTypes.Select, "Bo góc", BlockPropGroups.Display, "card",
                Options:
                [
                    new("card", "Theo theme var(--radius-card, 18px)"),
                    new("none", "Không bo (vuông vức)"),
                    new("sm", "Nhỏ (4px)"),
                    new("md", "Vừa (8px)"),
                    new("lg", "Lớn (12px)"),
                    new("full", "Tròn hoàn toàn")
                ]),
            new("fit", BlockPropTypes.Select, "Kiểu lấp đầy", BlockPropGroups.Display, "cover",
                Options:
                [
                    new("cover", "Cắt tràn khung (cover)"),
                    new("contain", "Thu vừa khung (contain)")
                ]),
            new("maxHeight", BlockPropTypes.Text, "Chiều cao tối đa (CSS)", BlockPropGroups.Display,
                Placeholder: "vd. 450px hoặc 60vh",
                Hint: "Để trống = không giới hạn chiều cao.")
        ],
        DefaultPropsJson: "{\"ratio\":\"auto\",\"radius\":\"card\",\"fit\":\"cover\"}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var ratioProp = props.GetString("ratio") ?? "auto";
        var radiusProp = props.GetString("radius") ?? "card";
        var fitProp = props.GetString("fit") ?? "cover";
        var maxHeight = props.GetString("maxHeight");

        // fitProp đi thẳng vào style="..." nên chỉ nhận giá trị hợp lệ của object-fit.
        var fitStyle = fitProp switch
        {
            "contain" => "contain",
            "fill" => "fill",
            "none" => "none",
            "scale-down" => "scale-down",
            _ => "cover"
        };

        var radiusStyle = radiusProp switch
        {
            "none" => "0px",
            "sm" => "4px",
            "md" => "8px",
            "lg" => "12px",
            "full" => "9999px",
            _ => "var(--radius-card, 18px)"
        };

        var ratioStyle = ratioProp switch
        {
            "16:9" => "16/9",
            "4:3" => "4/3",
            "1:1" => "1/1",
            "21:9" => "21/9",
            _ => "auto"
        };

        var safeMaxHeight = BlockCss.Safe(maxHeight);
        var maxHeightStyle = safeMaxHeight is not null ? $"max-height:{safeMaxHeight};" : "";

        // Placeholder khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div style=\"border-radius:{radiusStyle};overflow:hidden;background:#f1f5f9;height:280px;display:flex;align-items:center;justify-content:center;color:#94a3b8;margin-bottom:24px;border:1px dashed #cbd5e1\">" +
                   "<div style=\"text-align:center\">" +
                   "<svg style=\"width:40px;height:40px;margin:0 auto 8px;display:block;opacity:0.6\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.5\"/><polyline points=\"21 15 16 10 5 21\"/></svg>" +
                   "<span style=\"font-size:0.9rem\">(Ảnh đại diện bài viết / sản phẩm mẫu)</span></div></div>";
        }

        var detail = await _contentTypes.LoadDetailAsync(context.Route.RouteType, context.Route.EntityId, ct);
        var imageUrl = detail?.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl))
            return string.Empty;

        var alt = detail?.ImageAlt ?? detail?.Title ?? context.Route.Slug;

        return $"<div style=\"border-radius:{radiusStyle};overflow:hidden;background:rgba(228,226,221,1);margin-bottom:28px;{maxHeightStyle}\">" +
               $"<img src=\"{WebUtility.HtmlEncode(imageUrl)}\" alt=\"{WebUtility.HtmlEncode(alt)}\" loading=\"lazy\" " +
               $"style=\"width:100%;height:auto;aspect-ratio:{ratioStyle};object-fit:{fitStyle};display:block\"></div>";
    }
}

