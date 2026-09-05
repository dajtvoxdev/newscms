using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block hiển thị danh sách chuyên mục. Props: { "type": "Post", "layout": "grid" }.
/// Render link đến route chuyên mục; preset quyết định là chip ngang, thẻ hay danh sách dọc.
/// </summary>
public sealed class CategoryListBlock : IDynamicBlock
{
    public string Key => "category-list";

    private readonly Persistence.AppDbContext _db;

    public CategoryListBlock(Persistence.AppDbContext db) => _db = db;

    public BlockDescriptor Descriptor => new(
        Label: "Danh sách chuyên mục",
        Category: "Dữ liệu",
        Description: "Các link tới chuyên mục, dạng chip ngang / thẻ / danh sách dọc.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z\"/>" +
                 "<line x1=\"7\" y1=\"7\" x2=\"7.01\" y2=\"7\"/></svg>",
        Presets:
        [
            new BlockPresetDescriptor("chips", "Chip ngang", SharedBlockDescriptors.ChipsThumb,
                "{\"layout\":\"grid\",\"columns\":0}"),
            new BlockPresetDescriptor("cards", "Thẻ nhiều cột", SharedBlockDescriptors.GridThumb(3),
                "{\"layout\":\"grid\",\"columns\":3}"),
            new BlockPresetDescriptor("stack", "Danh sách dọc", SharedBlockDescriptors.TitleOnlyThumb,
                "{\"layout\":\"list\",\"columns\":0}")
        ],
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            new BlockPropDescriptor("type", BlockPropTypes.Select, "Loại chuyên mục",
                BlockPropGroups.Data, "Post",
                Options:
                [
                    new BlockPropOption("Post", "Bài viết"),
                    new BlockPropOption("Product", "Sản phẩm"),
                    new BlockPropOption("Page", "Trang")
                ]),
            new BlockPropDescriptor("showCount", BlockPropTypes.Checkbox, "Hiện số chuyên mục con",
                BlockPropGroups.Content, "true"),
            SharedBlockDescriptors.EmptyText()
        ]);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var typeStr = props.GetString("type") ?? "Post";
        var showCount = props.GetBool("showCount", true);
        var type = Enum.TryParse<Domain.Enums.CategoryType>(typeStr, true, out var t) ? t : Domain.Enums.CategoryType.Post;

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == null && c.Type == type)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .Select(c => new { c.Name, c.Slug, ChildCount = c.Children.Count })
            .ToListAsync(ct);

        if (categories.Count == 0) return shared.EmptyState("<!-- category-list: empty -->");

        // Chip ngang (mặc định) khi không đặt số cột; đặt columns/list thì dùng container chung
        // để layout khớp đúng những gì preset hứa.
        var containerStyle = shared.Layout == "grid" && shared.Columns == 0
            ? $"list-style:none;padding:0;margin:0;display:flex;flex-wrap:wrap;gap:{shared.Gap ?? 12}px"
            : $"list-style:none;padding:0;margin:0;{shared.ContainerStyle(200, 12)}";

        var sb = new StringBuilder();
        sb.Append($"<ul style=\"{containerStyle}\">");
        foreach (var c in categories)
        {
            var count = showCount && c.ChildCount > 0
                ? $" <span style=\"color:#94a3b8;font-size:.8em\">({c.ChildCount})</span>"
                : "";
            sb.Append("<li style=\"break-inside:avoid\">");
            sb.Append($"<a href=\"/{System.Net.WebUtility.HtmlEncode(c.Slug)}\" style=\"display:block;padding:8px 16px;border:1px solid #e2e8f0;border-radius:8px;text-decoration:none;color:#0f172a;font-weight:600;background:#f8fafc\">");
            sb.Append(System.Net.WebUtility.HtmlEncode(c.Name));
            sb.Append(count);
            sb.Append("</a></li>");
        }
        sb.Append("</ul>");
        return sb.ToString();
    }
}
