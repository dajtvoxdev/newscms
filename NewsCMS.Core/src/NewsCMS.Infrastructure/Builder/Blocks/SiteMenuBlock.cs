using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Menu điều hướng lấy từ Admin → Menu theo <c>location</c> (header/footer/…).
/// Props: { "location": "header", "direction": "horizontal", "gap": 28, "depth": 1 }.
///
/// Vì sao là khối động chứ không phải mấy thẻ &lt;a&gt; gõ tay trong shell: trước đây link nav nằm
/// cứng trong CompiledHtml của shell, nên thêm một mục menu phải mở builder sửa HTML, và mỗi shell
/// giữ một bản sao riêng — sửa một nơi, quên nơi khác. Nay dữ liệu ở một chỗ (bảng Menus), shell chỉ
/// đặt chỗ.
///
/// Bố cục bằng CSS thuần, KHÔNG cần JS: dropdown hover đòi rule <c>:hover</c> mà inline style không
/// biểu diễn được, nên <c>depth=2</c> render menu con thành danh sách lồng luôn hiện (hợp footer /
/// menu dọc). Muốn dropdown thì viết CSS ở Custom CSS của layout, nhắm class
/// <c>.nc-menu</c> / <c>.nc-menu-sub</c> khối này luôn phát ra.
/// </summary>
public sealed class SiteMenuBlock : IDynamicBlock
{
    public string Key => "site-menu";

    private readonly Persistence.AppDbContext _db;

    public SiteMenuBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Menu điều hướng",
        Category: "Dữ liệu",
        Description: "Các mục menu quản lý ở Admin → Menu, theo vị trí (header/footer).",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<line x1=\"3\" y1=\"6\" x2=\"21\" y2=\"6\"/><line x1=\"3\" y1=\"12\" x2=\"21\" y2=\"12\"/>" +
                 "<line x1=\"3\" y1=\"18\" x2=\"21\" y2=\"18\"/></svg>",
        Presets:
        [
            new BlockPresetDescriptor("nav-header", "Thanh ngang (header)", SharedBlockDescriptors.ChipsThumb,
                "{\"direction\":\"horizontal\",\"gap\":28,\"depth\":1}"),
            new BlockPresetDescriptor("nav-footer", "Cột dọc (footer)", SharedBlockDescriptors.TitleOnlyThumb,
                "{\"direction\":\"vertical\",\"gap\":10,\"depth\":2}")
        ],
        Props:
        [
            new BlockPropDescriptor("location", BlockPropTypes.Select, "Vị trí menu",
                BlockPropGroups.Data, "header", OptionsSource: BlockOptionSources.MenuLocations,
                Hint: "Danh sách lấy từ Admin → Menu. Chưa có menu nào thì tạo ở đó trước."),
            new BlockPropDescriptor("direction", BlockPropTypes.Select, "Hướng",
                BlockPropGroups.Display, "horizontal",
                Options:
                [
                    new BlockPropOption("horizontal", "Ngang"),
                    new BlockPropOption("vertical", "Dọc")
                ]),
            new BlockPropDescriptor("gap", BlockPropTypes.Number, "Khoảng cách (px)",
                BlockPropGroups.Display, "28", Min: 0, Max: 64),
            new BlockPropDescriptor("depth", BlockPropTypes.Number, "Số cấp",
                BlockPropGroups.Display, "1", Min: 1, Max: 2,
                Hint: "2 = hiện luôn menu con dạng danh sách lồng (hợp menu dọc / footer)."),
            new BlockPropDescriptor("uppercase", BlockPropTypes.Checkbox, "CHỮ HOA",
                BlockPropGroups.Display, "false"),
            SharedBlockDescriptors.EmptyText()
        ]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);

        var location = props.GetString("location");
        if (string.IsNullOrWhiteSpace(location)) location = "header";

        var vertical = props.GetString("direction") == "vertical";
        var gap = Math.Clamp(props.GetInt("gap", vertical ? 10 : 28), 0, 64);
        var depth = Math.Clamp(props.GetInt("depth", 1), 1, 2);
        var upper = props.GetBool("uppercase", false);

        var menu = await _db.Menus.AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Location == location, ct);

        // Không có menu ở vị trí này là chuyện thường lúc mới dựng site — im lặng (comment) thay vì
        // đổ chữ lạ vào header, trừ khi người dùng tự đặt emptyText.
        if (menu is null || menu.Items.Count == 0)
            return shared.EmptyState($"<!-- site-menu: chưa có menu ở vị trí '{WebUtility.HtmlEncode(location)}' -->");

        var roots = menu.Items
            .Where(i => i.ParentId is null)
            .OrderBy(i => i.Order)
            .ToList();

        // Dữ liệu cũ có thể không có mục gốc nào (mọi mục đều trỏ ParentId tới bản ghi đã xoá).
        // Rơi về "phẳng hết" còn hơn render một menu rỗng.
        if (roots.Count == 0) roots = menu.Items.OrderBy(i => i.Order).ToList();

        var childrenByParent = menu.Items
            .Where(i => i.ParentId is not null)
            .GroupBy(i => i.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Order).ToList());

        var sb = new StringBuilder();
        var flow = vertical
            ? $"display:flex;flex-direction:column;gap:{gap}px;align-items:flex-start"
            : $"display:flex;flex-wrap:wrap;align-items:center;gap:{gap}px";
        var transform = upper ? ";text-transform:uppercase;letter-spacing:.08em" : "";

        sb.Append($"<nav class=\"nc-menu\" style=\"{flow}{transform}\">");
        foreach (var item in roots)
        {
            var kids = depth > 1 && childrenByParent.TryGetValue(item.Id, out var list) ? list : null;

            if (kids is null)
            {
                AppendLink(sb, item.Title, item.Url, item.Target);
                continue;
            }

            sb.Append("<div class=\"nc-menu-group\" style=\"display:flex;flex-direction:column;gap:8px;align-items:flex-start\">");
            AppendLink(sb, item.Title, item.Url, item.Target);
            sb.Append($"<div class=\"nc-menu-sub\" style=\"display:flex;flex-direction:column;gap:{Math.Max(4, gap / 2)}px;align-items:flex-start;font-size:.92em;opacity:.85\">");
            foreach (var kid in kids) AppendLink(sb, kid.Title, kid.Url, kid.Target);
            sb.Append("</div></div>");
        }
        sb.Append("</nav>");
        return sb.ToString();
    }

    /// <summary>
    /// Link kế thừa màu/font của phần tử bọc nó trong shell, nên chỉnh style ở builder là menu đổi
    /// theo — không hardcode màu để rồi lệch với theme.
    /// </summary>
    private static void AppendLink(StringBuilder sb, string title, string? url, string? target)
    {
        var href = string.IsNullOrWhiteSpace(url) ? "#" : url;
        var rel = target == "_blank" ? " target=\"_blank\" rel=\"noopener\"" : "";
        sb.Append($"<a class=\"nc-menu-link\" href=\"{WebUtility.HtmlEncode(href)}\"{rel} ")
          .Append("style=\"color:inherit;text-decoration:none;white-space:nowrap\">")
          .Append(WebUtility.HtmlEncode(title))
          .Append("</a>");
    }
}
