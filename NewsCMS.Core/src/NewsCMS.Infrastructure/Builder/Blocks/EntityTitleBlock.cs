using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị tiêu đề của thực thể đang xem (bài viết, sản phẩm, hoặc chuyên mục).
/// Tự động lấy tên thực thể từ DbContext dựa trên RouteContext của template.
/// Nếu đặt ở trang thường không có route entity, hiển thị placeholder mẫu mà không throw exception.
/// </summary>
public sealed class EntityTitleBlock : IDynamicBlock
{
    public string Key => "entity-title";

    private readonly IContentTypeRegistry _contentTypes;

    public EntityTitleBlock(IContentTypeRegistry contentTypes) => _contentTypes = contentTypes;

    public BlockDescriptor Descriptor => new(
        Label: "Tiêu đề chi tiết",
        Category: "Chi tiết",
        Description: "Tiêu đề của bài viết, sản phẩm hoặc chuyên mục đang xem.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M4 7V4h16v3\"/><path d=\"M9 20h6\"/><path d=\"M12 4v16\"/></svg>",
        Presets:
        [
            new("h1-large", "Tiêu đề H1 lớn",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"8\" width=\"48\" height=\"8\" rx=\"2\"/><rect x=\"0\" y=\"20\" width=\"32\" height=\"4\" rx=\"2\"/></svg>",
                "{\"tag\":\"h1\",\"size\":\"default\",\"align\":\"left\"}"),
            new("h1-center", "Tiêu đề căn giữa",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"8\" y=\"8\" width=\"32\" height=\"8\" rx=\"2\"/><rect x=\"14\" y=\"20\" width=\"20\" height=\"4\" rx=\"2\"/></svg>",
                "{\"tag\":\"h1\",\"size\":\"default\",\"align\":\"center\"}"),
            new("h2-medium", "Tiêu đề H2 vừa",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"10\" width=\"40\" height=\"6\" rx=\"2\"/><rect x=\"0\" y=\"20\" width=\"24\" height=\"4\" rx=\"2\"/></svg>",
                "{\"tag\":\"h2\",\"size\":\"lg\",\"align\":\"left\"}")
        ],
        Props:
        [
            new("tag", BlockPropTypes.Select, "Thẻ HTML", BlockPropGroups.Display, "h1",
                Options:
                [
                    new("h1", "H1 (tiêu đề chính)"),
                    new("h2", "H2 (tiêu đề phụ)"),
                    new("h3", "H3"),
                    new("div", "Thẻ div")
                ]),
            new("align", BlockPropTypes.Select, "Căn lề", BlockPropGroups.Display, "left",
                Options:
                [
                    new("left", "Trái"),
                    new("center", "Giữa"),
                    new("right", "Phải")
                ]),
            new("size", BlockPropTypes.Select, "Cỡ chữ", BlockPropGroups.Display, "default",
                Options:
                [
                    new("default", "Mặc định (co giãn theo màn hình)"),
                    new("sm", "Nhỏ (1.25rem)"),
                    new("md", "Vừa (1.5rem)"),
                    new("lg", "Lớn (1.85rem)"),
                    new("xl", "Rất lớn (2.25rem)"),
                    new("2xl", "Khổng lồ (2.75rem)")
                ]),
            new("color", BlockPropTypes.Text, "Màu chữ (CSS)", BlockPropGroups.Display,
                Placeholder: "vd. var(--color-brand-500) hoặc #0f172a",
                Hint: "Để trống = sử dụng biến màu var(--color-brand-500) của theme.")
        ],
        DefaultPropsJson: "{\"tag\":\"h1\",\"align\":\"left\",\"size\":\"default\"}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var tag = props.GetString("tag") ?? "h1";
        if (tag != "h1" && tag != "h2" && tag != "h3" && tag != "div") tag = "h1";

        var align = props.GetString("align") ?? "left";
        if (align != "left" && align != "center" && align != "right") align = "left";

        var sizeProp = props.GetString("size") ?? "default";
        var fontSize = sizeProp switch
        {
            "sm" => "1.25rem",
            "md" => "1.5rem",
            "lg" => "1.85rem",
            "xl" => "2.25rem",
            "2xl" => "2.75rem",
            _ => "clamp(1.8rem,4vw,2.6rem)"
        };

        // color đi thẳng vào style="..." — chỉ nhận giá trị CSS an toàn, không thì dùng mặc định.
        var color = BlockCss.Safe(props.GetString("color")) ?? "var(--color-brand-500)";

        var style = $"margin:12px 0 10px;font-family:var(--font-display);font-weight:400;font-size:{fontSize};line-height:1.25;color:{color};text-align:{align};word-break:break-word;";

        // Khi không có RouteContext (kéo vào trang tĩnh hoặc xem trước chưa chọn entity)
        if (context.Route is null)
        {
            return $"<{tag} data-nc-part=\"title\" style=\"{style};opacity:0.85\">(Tiêu đề bài viết / sản phẩm mẫu)</{tag}>";
        }

        var detail = await _contentTypes.LoadDetailAsync(context.Route.RouteType, context.Route.EntityId, ct);
        var title = detail?.Title ?? context.Route.Slug;

        return $"<{tag} data-nc-part=\"title\" style=\"{style}\">{WebUtility.HtmlEncode(title)}</{tag}>";
    }
}

