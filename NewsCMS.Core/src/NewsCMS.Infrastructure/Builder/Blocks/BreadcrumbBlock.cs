using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Builder.ContentTypes;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block breadcrumb dựa trên SiteRoute + ContentTypeRegistry. Props: { "path": "/tin-tuc/bai-viet" }.
/// Nếu không truyền path, tự động lấy path hiện tại từ RouteContext.
/// </summary>
public sealed class BreadcrumbBlock : IDynamicBlock
{
    public string Key => "breadcrumb";

    private readonly Persistence.AppDbContext _db;
    private readonly IContentTypeRegistry _contentTypes;

    public BreadcrumbBlock(Persistence.AppDbContext db, IContentTypeRegistry contentTypes)
    {
        _db = db;
        _contentTypes = contentTypes;
    }

    public BlockDescriptor Descriptor => new(
        Label: "Breadcrumb",
        Category: "Nội dung",
        Description: "Đường dẫn phân cấp, lấy tên hiển thị từ SiteRoute.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M3 6h6l3 6-3 6H3l3-6Z\"/><path d=\"M13 6h6l3 6-3 6h-6l3-6Z\"/></svg>",
        Presets: [],
        Props:
        [
            new BlockPropDescriptor("path", BlockPropTypes.Text, "Đường dẫn", BlockPropGroups.Data,
                Placeholder: "/tin-tuc/bai-viet",
                Hint: "Để trống = tự động lấy từ đường dẫn trang/thực thể hiện tại.")
        ],
        DefaultPropsJson: "{}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var rawPath = props.GetString("path");

        // Nếu không truyền path, tự động lấy từ RouteContext (URL hiện tại)
        var path = !string.IsNullOrWhiteSpace(rawPath) ? rawPath.Trim() : (context.Route?.Path ?? "/");

        // Parse path thành segments để build breadcrumb
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "<nav aria-label=\"Breadcrumb\"><ol style=\"list-style:none;padding:0;margin:0;display:flex;gap:8px;font-size:.9rem;\"><li><a href=\"/\" style=\"color:#0d7c66;text-decoration:none;\">Trang chủ</a></li></ol></nav>";

        var sb = new StringBuilder();
        sb.Append("<nav aria-label=\"Breadcrumb\"><ol style=\"list-style:none;padding:0;margin:0;display:flex;flex-wrap:wrap;gap:8px;font-size:.9rem;\">");
        sb.Append("<li><a href=\"/\" style=\"color:#0d7c66;text-decoration:none;\">Trang chủ</a></li>");

        var currentPath = "";
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath += "/" + segments[i];
            var isLast = i == segments.Length - 1;

            sb.Append("<li style=\"color:#94a3b8;\">/</li>");

            // Tra tên hiển thị từ SiteRoute → entity
            var route = await _db.SiteRoutes.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Path == currentPath && r.Culture == context.Culture, ct);

            string label = segments[i]; // fallback: slug segment
            if (route?.TargetId is { } targetId)
            {
                if (route.RouteType == Domain.Enums.RouteType.Page)
                {
                    label = await _db.Pages.Where(p => p.Id == targetId).Select(p => p.Title).FirstOrDefaultAsync(ct) ?? label;
                }
                else
                {
                    var detail = await _contentTypes.LoadDetailAsync(route.RouteType, targetId, ct);
                    if (detail is not null) label = detail.Title;
                }
            }
            else if (isLast && context.Route is { } activeRoute && string.Equals(currentPath, activeRoute.Path, StringComparison.OrdinalIgnoreCase))
            {
                // Fallback nếu route chưa ghi bảng SiteRoutes nhưng đã có trong RouteContext
                if (activeRoute.RouteType == Domain.Enums.RouteType.Page)
                {
                    label = await _db.Pages.Where(p => p.Id == activeRoute.EntityId).Select(p => p.Title).FirstOrDefaultAsync(ct) ?? label;
                }
                else
                {
                    var detail = await _contentTypes.LoadDetailAsync(activeRoute.RouteType, activeRoute.EntityId, ct);
                    if (detail is not null) label = detail.Title;
                }
            }

            if (isLast)
                sb.Append($"<li aria-current=\"page\" style=\"color:#334155;font-weight:600;\">{System.Net.WebUtility.HtmlEncode(label)}</li>");
            else
                sb.Append($"<li><a href=\"{System.Net.WebUtility.HtmlEncode(currentPath)}\" style=\"color:#0d7c66;text-decoration:none;\">{System.Net.WebUtility.HtmlEncode(label)}</a></li>");
        }

        sb.Append("</ol></nav>");
        return sb.ToString();
    }
}
