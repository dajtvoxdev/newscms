using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị tóm tắt ngắn hoặc đoạn sapo mở đầu của bài viết / sản phẩm.
/// Tự động lấy Post.Excerpt hoặc Product.ShortDescription.
/// </summary>
public sealed class EntityExcerptBlock : IDynamicBlock
{
    public string Key => "entity-excerpt";

    private readonly IContentTypeRegistry _contentTypes;

    public EntityExcerptBlock(IContentTypeRegistry contentTypes) => _contentTypes = contentTypes;

    public BlockDescriptor Descriptor => new(
        Label: "Mô tả ngắn / Sapo",
        Category: "Chi tiết",
        Description: "Tóm tắt ngắn hoặc đoạn sapo mở đầu bài viết / sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<line x1=\"17\" y1=\"10\" x2=\"3\" y2=\"10\"/><line x1=\"21\" y1=\"6\" x2=\"3\" y2=\"6\"/>" +
                 "<line x1=\"21\" y1=\"14\" x2=\"3\" y2=\"14\"/><line x1=\"17\" y1=\"18\" x2=\"3\" y2=\"18\"/></svg>",
        Presets:
        [
            new("sapo-editorial", "Sapo báo chí trang nhã",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"6\" width=\"48\" height=\"6\" rx=\"1.5\"/><rect x=\"0\" y=\"16\" width=\"48\" height=\"6\" rx=\"1.5\"/><rect x=\"0\" y=\"26\" width=\"32\" height=\"4\" rx=\"1\"/></svg>",
                "{\"size\":\"lg\",\"color\":\"ink\",\"align\":\"left\"}"),
            new("center-intro", "Tóm tắt căn giữa",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"6\" y=\"8\" width=\"36\" height=\"6\" rx=\"1.5\"/><rect x=\"10\" y=\"18\" width=\"28\" height=\"5\" rx=\"1\"/></svg>",
                "{\"size\":\"lg\",\"color\":\"ink\",\"align\":\"center\"}"),
            new("compact-muted", "Gọn gàng chữ phụ",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"10\" width=\"44\" height=\"4\" rx=\"1\"/><rect x=\"0\" y=\"18\" width=\"36\" height=\"4\" rx=\"1\"/></svg>",
                "{\"size\":\"base\",\"color\":\"muted\",\"align\":\"left\"}")
        ],
        Props:
        [
            new("size", BlockPropTypes.Select, "Cỡ chữ", BlockPropGroups.Display, "lg",
                Options:
                [
                    new("sm", "Nhỏ (0.95rem)"),
                    new("base", "Vừa (1rem)"),
                    new("lg", "Lớn / Sapo (1.05rem)"),
                    new("xl", "Rất lớn (1.2rem)")
                ]),
            new("color", BlockPropTypes.Select, "Màu sắc", BlockPropGroups.Display, "ink",
                Options:
                [
                    new("ink", "Màu chữ chính (var(--color-ink))"),
                    new("muted", "Màu xám mờ (var(--color-muted))"),
                    new("brand", "Màu thương hiệu (var(--color-brand-500))")
                ]),
            new("align", BlockPropTypes.Select, "Căn lề", BlockPropGroups.Display, "left",
                Options:
                [
                    new("left", "Trái"),
                    new("center", "Giữa"),
                    new("justify", "Căn đều hai bên")
                ])
        ],
        DefaultPropsJson: "{\"size\":\"lg\",\"color\":\"ink\",\"align\":\"left\"}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var sizeProp = props.GetString("size") ?? "lg";
        var colorProp = props.GetString("color") ?? "ink";
        var align = props.GetString("align") ?? "left";

        var fontSize = sizeProp switch
        {
            "sm" => "0.95rem",
            "base" => "1rem",
            "xl" => "1.2rem",
            _ => "1.05rem"
        };

        var color = colorProp switch
        {
            "muted" => "var(--color-muted,#64748b)",
            "brand" => "var(--color-brand-500,#0d7c66)",
            _ => "var(--color-ink,#1e293b)"
        };

        var opacity = colorProp == "ink" ? "opacity:.9;" : "";
        var style = $"margin:0 0 28px;font-size:{fontSize};line-height:1.75;color:{color};{opacity}text-align:{align};";

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<p style=\"{style}\">(Đoạn mô tả ngắn hoặc sapo mở đầu của bài viết / sản phẩm mẫu...)</p>";
        }

        var detail = await _contentTypes.LoadDetailAsync(context.Route.RouteType, context.Route.EntityId, ct);
        if (detail is null || string.IsNullOrWhiteSpace(detail.Excerpt))
            return string.Empty;

        return $"<p style=\"{style}\">{WebUtility.HtmlEncode(detail.Excerpt)}</p>";
    }
}

