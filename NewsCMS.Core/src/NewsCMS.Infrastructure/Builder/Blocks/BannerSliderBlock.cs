using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block hiển thị banner slider từ Banner entity. Props: { "position": "home-hero", "limit": 5 }.
/// Render dạng simple slider (CSS-only, không JS dependency).
/// </summary>
public sealed class BannerSliderBlock : IDynamicBlock
{
    public string Key => "banner-slider";

    private readonly Persistence.AppDbContext _db;

    public BannerSliderBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Banner slider",
        Category: "Dữ liệu",
        Description: "Banner quản lý ở menu Banner, trượt ngang.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"m6 12h.01M10 12h.01\"/>" +
                 "<path d=\"m14 9 3 3-3 3\"/></svg>",
        Presets: [],
        Props:
        [
            new BlockPropDescriptor("position", BlockPropTypes.Select, "Vị trí banner",
                BlockPropGroups.Data, "home-hero", OptionsSource: BlockOptionSources.BannerPositions),
            new BlockPropDescriptor("limit", BlockPropTypes.Number, "Số banner tối đa",
                BlockPropGroups.Data, "5", Min: 1, Max: 20),
            new BlockPropDescriptor("height", BlockPropTypes.Number, "Chiều cao ảnh (px)",
                BlockPropGroups.Display, "400", Min: 120, Max: 900),
            SharedBlockDescriptors.EmptyText()
        ],
        DefaultPropsJson: "{\"position\":\"home-hero\",\"limit\":5}");

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var position = props.GetString("position") ?? "home-hero";
        var limit = Math.Clamp(props.GetInt("limit", 5), 1, 20);
        var height = Math.Clamp(props.GetInt("height", 400), 120, 900);

        var now = DateTime.UtcNow;
        var banners = await _db.Banners.AsNoTracking()
            .Where(b => b.IsActive && b.Position == position &&
                        (b.StartAt == null || b.StartAt <= now) &&
                        (b.EndAt == null || b.EndAt >= now))
            .OrderBy(b => b.Order)
            .Take(limit)
            .Select(b => new
            {
                b.Title,
                ImageUrl = _db.Medias.Where(m => m.Id == b.ImageId).Select(m => m.FilePath).FirstOrDefault(),
                b.Url
            })
            .ToListAsync(ct);

        if (banners.Count == 0) return shared.EmptyState("<!-- banner-slider: no banners -->");

        var sb = new StringBuilder();
        sb.Append("<div style=\"overflow:hidden;border-radius:12px\">");
        sb.Append("<div style=\"display:flex;overflow-x:auto;scroll-snap-type:x mandatory;-webkit-overflow-scrolling:touch\">");
        foreach (var b in banners)
        {
            var img = $"<img src=\"{System.Net.WebUtility.HtmlEncode(b.ImageUrl)}\" alt=\"{System.Net.WebUtility.HtmlEncode(b.Title)}\" loading=\"lazy\"" +
                      $" style=\"width:100%;min-width:100%;height:{height}px;object-fit:cover;scroll-snap-align:start;display:block\">";
            if (!string.IsNullOrEmpty(b.Url))
                sb.Append($"<a href=\"{System.Net.WebUtility.HtmlEncode(b.Url)}\" style=\"flex:0 0 100%\">{img}</a>");
            else
                sb.Append($"<div style=\"flex:0 0 100%\">{img}</div>");
        }
        sb.Append("</div></div>");
        return sb.ToString();
    }
}
