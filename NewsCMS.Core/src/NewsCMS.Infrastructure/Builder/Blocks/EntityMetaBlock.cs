using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị các thông tin meta của bài viết hoặc sản phẩm:
/// ngày đăng, chuyên mục (có link liên kết), tác giả và lượt xem.
/// </summary>
public sealed class EntityMetaBlock : IDynamicBlock
{
    public string Key => "entity-meta";

    private readonly IContentTypeRegistry _contentTypes;

    public EntityMetaBlock(IContentTypeRegistry contentTypes) => _contentTypes = contentTypes;

    public BlockDescriptor Descriptor => new(
        Label: "Thông tin meta chi tiết",
        Category: "Chi tiết",
        Description: "Ngày đăng, chuyên mục, tác giả và lượt xem của bài viết/sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<circle cx=\"12\" cy=\"12\" r=\"10\"/><polyline points=\"12 6 12 12 16 14\"/></svg>",
        Presets:
        [
            new("date-category", "Chuyên mục + Ngày đăng",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"12\" width=\"18\" height=\"8\" rx=\"2\"/><rect x=\"22\" y=\"13\" width=\"24\" height=\"6\" rx=\"1.5\"/></svg>",
                "{\"showCategory\":true,\"showDate\":true,\"showAuthor\":false,\"showViews\":false}"),
            new("full-meta", "Đầy đủ thông tin",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"12\" width=\"12\" height=\"8\" rx=\"2\"/><rect x=\"15\" y=\"13\" width=\"14\" height=\"6\" rx=\"1.5\"/><rect x=\"32\" y=\"13\" width=\"14\" height=\"6\" rx=\"1.5\"/></svg>",
                "{\"showCategory\":true,\"showDate\":true,\"showAuthor\":true,\"showViews\":true}"),
            new("date-only", "Chỉ ngày đăng",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"13\" width=\"28\" height=\"6\" rx=\"1.5\"/></svg>",
                "{\"showCategory\":false,\"showDate\":true,\"showAuthor\":false,\"showViews\":false}")
        ],
        Props:
        [
            new("showCategory", BlockPropTypes.Checkbox, "Hiện chuyên mục", BlockPropGroups.Content, "true"),
            new("showDate", BlockPropTypes.Checkbox, "Hiện ngày đăng", BlockPropGroups.Content, "true"),
            new("showAuthor", BlockPropTypes.Checkbox, "Hiện tác giả", BlockPropGroups.Content, "false"),
            new("showViews", BlockPropTypes.Checkbox, "Hiện lượt xem", BlockPropGroups.Content, "false"),
            new("dateFormat", BlockPropTypes.Select, "Định dạng ngày", BlockPropGroups.Display, "dd.MM.yyyy",
                Options:
                [
                    new("dd.MM.yyyy", "dd.MM.yyyy (vd. 03.09.2026)"),
                    new("dd/MM/yyyy", "dd/MM/yyyy (vd. 03/09/2026)"),
                    new("yyyy-MM-dd", "yyyy-MM-dd (vd. 2026-09-03)")
                ]),
            new("align", BlockPropTypes.Select, "Căn lề", BlockPropGroups.Display, "left",
                Options:
                [
                    new("left", "Trái"),
                    new("center", "Giữa"),
                    new("right", "Phải")
                ])
        ],
        DefaultPropsJson: "{\"showCategory\":true,\"showDate\":true,\"showAuthor\":false,\"showViews\":false,\"dateFormat\":\"dd.MM.yyyy\"}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var showCategory = props.GetBool("showCategory", true);
        var showDate = props.GetBool("showDate", true);
        var showAuthor = props.GetBool("showAuthor", false);
        var showViews = props.GetBool("showViews", false);
        var dateFormat = props.GetString("dateFormat") ?? "dd.MM.yyyy";
        var align = props.GetString("align") ?? "left";

        var justify = align switch
        {
            "center" => "center",
            "right" => "flex-end",
            _ => "flex-start"
        };

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div data-nc-part=\"wrapper\" style=\"color:var(--color-muted,#94a3b8);font-size:0.85rem;margin-bottom:24px;display:flex;flex-wrap:wrap;align-items:center;gap:12px;justify-content:{justify}\">" +
                   (showCategory ? "<a data-nc-part=\"category\" href=\"#\" style=\"font-size:0.72rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500,#0d7c66);text-decoration:none\">CHUYÊN MỤC</a>" : "") +
                   (showDate ? $"<span data-nc-part=\"date\">{DateTime.Now.ToString(dateFormat, CultureInfo.GetCultureInfo("vi-VN"))}</span>" : "") +
                   (showAuthor ? "<span data-nc-part=\"author\">Tác giả mẫu</span>" : "") +
                   (showViews ? "<span data-nc-part=\"views\">1.234 lượt xem</span>" : "") +
                   "</div>";
        }

        var detail = await _contentTypes.LoadDetailAsync(context.Route.RouteType, context.Route.EntityId, ct);
        if (detail is null) return string.Empty;

        var publishedAt = detail.PublishedAt;
        var categoryName = detail.CategoryName;
        var categorySlug = detail.CategorySlug;
        string? authorName = null;
        if (detail.Extra.TryGetValue("AuthorName", out var aObj) && aObj is string aStr) authorName = aStr;
        long viewCount = 0;
        if (detail.Extra.TryGetValue("ViewCount", out var vObj))
        {
            if (vObj is long vLong) viewCount = vLong;
            else if (vObj is int vInt) viewCount = vInt;
        }

        var sb = new StringBuilder();
        sb.Append($"<div data-nc-part=\"wrapper\" style=\"color:var(--color-muted);font-size:0.85rem;margin-bottom:24px;display:flex;flex-wrap:wrap;align-items:center;gap:12px;justify-content:{justify}\">");

        if (showCategory && !string.IsNullOrWhiteSpace(categoryName))
        {
            var href = !string.IsNullOrWhiteSpace(categorySlug) ? $"/{WebUtility.HtmlEncode(categorySlug)}" : "#";
            sb.Append($"<a data-nc-part=\"category\" href=\"{href}\" style=\"font-size:0.72rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500);text-decoration:none\">");
            sb.Append(WebUtility.HtmlEncode(categoryName));
            sb.Append("</a>");
        }

        if (showDate && publishedAt.HasValue)
        {
            sb.Append("<span data-nc-part=\"date\">");
            sb.Append(publishedAt.Value.ToString(dateFormat, CultureInfo.GetCultureInfo("vi-VN")));
            sb.Append("</span>");
        }

        if (showAuthor && !string.IsNullOrWhiteSpace(authorName))
        {
            sb.Append($"<span data-nc-part=\"author\">Tác giả: {WebUtility.HtmlEncode(authorName)}</span>");
        }

        if (showViews && viewCount > 0)
        {
            sb.Append($"<span data-nc-part=\"views\">{viewCount:N0} lượt xem</span>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }
}

