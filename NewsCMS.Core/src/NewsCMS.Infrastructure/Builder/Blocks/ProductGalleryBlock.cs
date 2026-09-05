using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị bộ sưu tập ảnh sản phẩm với 2 kiểu hiển thị:
/// 1. "thumbnails": Ảnh chính lớn kèm dải ảnh thu nhỏ phía dưới, click đổi ảnh tức thì.
/// 2. "grid": Lưới các ảnh sản phẩm xếp liền kề phong cách lookbook/editorial.
/// </summary>
public sealed class ProductGalleryBlock : IDynamicBlock
{
    public string Key => "product-gallery";

    private readonly AppDbContext _db;

    public ProductGalleryBlock(AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Bộ sưu tập ảnh sản phẩm",
        Category: "Chi tiết",
        Description: "Ảnh chính kèm dải ảnh thu nhỏ hoặc lưới ảnh sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" rx=\"5\"/><path d=\"M16 2v20\"/><path d=\"M2 12h14\"/></svg>",
        Presets:
        [
            new("thumbnails", "Ảnh chính + dải ảnh nhỏ",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"4\" y=\"2\" width=\"40\" height=\"20\" rx=\"2\"/><rect x=\"4\" y=\"25\" width=\"8\" height=\"5\" rx=\"1\"/><rect x=\"15\" y=\"25\" width=\"8\" height=\"5\" rx=\"1\"/><rect x=\"26\" y=\"25\" width=\"8\" height=\"5\" rx=\"1\"/><rect x=\"36\" y=\"25\" width=\"8\" height=\"5\" rx=\"1\"/></svg>",
                "{\"preset\":\"thumbnails\",\"radius\":\"card\"}"),
            new("grid-2", "Lưới 2 cột lookbook",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"2\" y=\"2\" width=\"21\" height=\"13\" rx=\"2\"/><rect x=\"25\" y=\"2\" width=\"21\" height=\"13\" rx=\"2\"/><rect x=\"2\" y=\"17\" width=\"21\" height=\"13\" rx=\"2\"/><rect x=\"25\" y=\"17\" width=\"21\" height=\"13\" rx=\"2\"/></svg>",
                "{\"preset\":\"grid\",\"columns\":2,\"radius\":\"md\"}"),
            new("stacked", "Ảnh xếp dọc nối tiếp",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"4\" y=\"2\" width=\"40\" height=\"12\" rx=\"2\"/><rect x=\"4\" y=\"17\" width=\"40\" height=\"12\" rx=\"2\"/></svg>",
                "{\"preset\":\"stacked\",\"radius\":\"card\"}")
        ],
        Props:
        [
            new("preset", BlockPropTypes.Select, "Kiểu hiển thị", BlockPropGroups.Display, "thumbnails",
                Options:
                [
                    new("thumbnails", "Ảnh chính + dải ảnh nhỏ bên dưới"),
                    new("grid", "Lưới ảnh (lookbook)"),
                    new("stacked", "Xếp dọc nối tiếp")
                ]),
            new("columns", BlockPropTypes.Number, "Số cột (chỉ áp dụng cho lưới)", BlockPropGroups.Display, "2",
                Min: 1, Max: 4),
            new("gap", BlockPropTypes.Number, "Khoảng cách giữa các ảnh (px)", BlockPropGroups.Display, "12",
                Min: 0, Max: 32),
            new("radius", BlockPropTypes.Select, "Bo góc", BlockPropGroups.Display, "card",
                Options:
                [
                    new("card", "Theo theme var(--radius-card, 18px)"),
                    new("none", "Không bo"),
                    new("sm", "Nhỏ (4px)"),
                    new("md", "Vừa (8px)"),
                    new("lg", "Lớn (12px)")
                ])
        ],
        DefaultPropsJson: "{\"preset\":\"thumbnails\",\"columns\":2,\"gap\":12,\"radius\":\"card\"}",
        EntityScoped: true,
        SupportedTypes: ["product"]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var preset = props.GetString("preset") ?? "thumbnails";
        var columns = Math.Clamp(props.GetInt("columns", 2), 1, 4);
        var gap = Math.Clamp(props.GetInt("gap", 12), 0, 32);
        var radiusProp = props.GetString("radius") ?? "card";

        var radius = radiusProp switch
        {
            "none" => "0px",
            "sm" => "4px",
            "md" => "8px",
            "lg" => "12px",
            _ => "var(--radius-card, 18px)"
        };

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div style=\"margin-bottom:24px\">" +
                   $"<div style=\"border-radius:{radius};overflow:hidden;background:#f1f5f9;height:320px;display:flex;align-items:center;justify-content:center;color:#94a3b8;border:1px dashed #cbd5e1;margin-bottom:12px\">" +
                   "<span>(Ảnh sản phẩm mẫu)</span></div>" +
                   "<div style=\"display:flex;gap:8px\">" +
                   "<div style=\"width:60px;height:60px;background:#e2e8f0;border-radius:6px\"></div>" +
                   "<div style=\"width:60px;height:60px;background:#e2e8f0;border-radius:6px\"></div>" +
                   "<div style=\"width:60px;height:60px;background:#e2e8f0;border-radius:6px\"></div>" +
                   "</div></div>";
        }

        if (context.Route.RouteType != RouteType.Product)
            return string.Empty;

        var entityId = context.Route.EntityId;
        var p = await _db.Products.AsNoTracking()
            .Where(x => x.Id == entityId)
            .Select(x => new
            {
                Name = x.Name,
                Thumbnail = x.Thumbnail != null ? x.Thumbnail.FilePath : null,
                Images = x.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Url, i.AltText }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (p is null) return string.Empty;

        // Tập hợp danh sách ảnh (thumbnail + images bổ sung)
        var allImages = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Thumbnail))
            allImages.Add(p.Thumbnail);

        foreach (var img in p.Images)
        {
            if (!string.IsNullOrWhiteSpace(img.Url) && !allImages.Contains(img.Url))
                allImages.Add(img.Url);
        }

        if (allImages.Count == 0)
            return string.Empty;

        var productName = WebUtility.HtmlEncode(p.Name);
        var galleryUid = "nc-gal-" + Math.Abs(entityId.GetHashCode());

        var sb = new StringBuilder();
        sb.Append($"<div class=\"nc-product-gallery\" style=\"margin-bottom:24px\">");

        if (preset == "grid")
        {
            sb.Append($"<div style=\"display:grid;grid-template-columns:repeat({columns}, minmax(0, 1fr));gap:{gap}px;\">");
            for (var i = 0; i < allImages.Count; i++)
            {
                var imgUrl = WebUtility.HtmlEncode(allImages[i]);
                sb.Append($"<div style=\"border-radius:{radius};overflow:hidden;background:rgba(228,226,221,1);\">");
                sb.Append($"<img src=\"{imgUrl}\" alt=\"{productName} - ảnh {i + 1}\" loading=\"lazy\" style=\"width:100%;height:auto;aspect-ratio:1/1;object-fit:cover;display:block\">");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }
        else if (preset == "stacked")
        {
            sb.Append($"<div style=\"display:flex;flex-direction:column;gap:{gap}px;\">");
            for (var i = 0; i < allImages.Count; i++)
            {
                var imgUrl = WebUtility.HtmlEncode(allImages[i]);
                sb.Append($"<div style=\"border-radius:{radius};overflow:hidden;background:rgba(228,226,221,1);\">");
                sb.Append($"<img src=\"{imgUrl}\" alt=\"{productName} - ảnh {i + 1}\" loading=\"lazy\" style=\"width:100%;height:auto;display:block\">");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }
        else // thumbnails
        {
            var firstUrl = WebUtility.HtmlEncode(allImages[0]);
            sb.Append($"<div style=\"border-radius:{radius};overflow:hidden;background:rgba(228,226,221,1);margin-bottom:12px;\">");
            sb.Append($"<img id=\"{galleryUid}-main\" src=\"{firstUrl}\" alt=\"{productName}\" style=\"width:100%;height:auto;aspect-ratio:1/1;object-fit:cover;display:block\">");
            sb.Append("</div>");

            if (allImages.Count > 1)
            {
                sb.Append("<div style=\"display:flex;gap:8px;overflow-x:auto;padding-bottom:4px;\">");
                for (var i = 0; i < allImages.Count; i++)
                {
                    var imgUrl = WebUtility.HtmlEncode(allImages[i]);
                    var isSelected = i == 0;
                    var border = isSelected ? "2px solid var(--color-brand-500, #0d7c66)" : "1px solid #e2e8f0";
                    sb.Append($"<button type=\"button\" onclick=\"document.getElementById('{galleryUid}-main').src='{imgUrl}';\" " +
                              $"style=\"flex-shrink:0;padding:0;background:none;border:{border};border-radius:8px;overflow:hidden;cursor:pointer;width:64px;height:64px;\">");
                    sb.Append($"<img src=\"{imgUrl}\" alt=\"thumb {i + 1}\" loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover;display:block\">");
                    sb.Append("</button>");
                }
                sb.Append("</div>");
            }
        }

        sb.Append("</div>");
        return sb.ToString();
    }
}

