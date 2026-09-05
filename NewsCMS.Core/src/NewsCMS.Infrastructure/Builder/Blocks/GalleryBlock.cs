using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối Thư viện ảnh: lấy ảnh từ <c>Medias</c> / <c>MediaFolders</c> theo thư mục hoặc theo danh
/// sách chọn tay, render masonry / lưới vuông / carousel / justified.
///
/// Hai quy tắc bắt buộc, cùng lý do là bug "canvas vỡ bố cục" trước đây:
/// 1) Bố cục thành hình bằng CSS thuần (<c>column-count</c> cho masonry, không dùng Masonry.js).
/// 2) Mỗi <c>&lt;img&gt;</c> có <c>loading="lazy"</c> + <c>width</c>/<c>height</c> thật để trình duyệt
///    giữ chỗ trước khi ảnh về, không gây layout shift.
///
/// Lightbox là JS TĂNG CƯỜNG: tắt JS thì ảnh vẫn xếp đúng, chỉ mất phần bấm để phóng to.
/// </summary>
public sealed class GalleryBlock : IDynamicBlock, IBlockMatchCounter
{
    public string Key => "gallery";

    private const int DefaultCount = 12;
    private const int MaxCount = 60;
    private const int MinTileWidth = 220;

    /// <summary>Lightbox script — same-origin nên qua được CSP <c>script-src 'self'</c>.</summary>
    private const string JsPath = "/js/nc-gallery.js";

    private readonly Persistence.AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public GalleryBlock(Persistence.AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public BlockDescriptor Descriptor => new(
        Label: "Thư viện ảnh",
        Category: "Dữ liệu",
        Description: "Ảnh từ thư mục Media hoặc danh sách chọn tay, kèm lightbox.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.5\"/>" +
                 "<path d=\"m21 15-5-5L5 21\"/></svg>",
        Presets:
        [
            new BlockPresetDescriptor("masonry", "Masonry (so le)", SharedBlockDescriptors.MasonryThumb,
                "{\"layout\":\"masonry\",\"columns\":3,\"crop\":false}"),
            new BlockPresetDescriptor("square", "Lưới vuông", SharedBlockDescriptors.SquareGridThumb,
                "{\"layout\":\"grid\",\"columns\":3,\"crop\":true}"),
            new BlockPresetDescriptor("carousel", "Cuộn ngang", SharedBlockDescriptors.CarouselThumb,
                "{\"layout\":\"carousel\",\"columns\":0,\"crop\":true}"),
            new BlockPresetDescriptor("justified", "Hàng đều nhau", SharedBlockDescriptors.ListThumb,
                "{\"layout\":\"list\",\"columns\":0,\"crop\":true}")
        ],
        Props:
        [
            SharedBlockDescriptors.Layout(),
            SharedBlockDescriptors.Columns(),
            SharedBlockDescriptors.Gap(),
            new BlockPropDescriptor("ratio", BlockPropTypes.Select, "Tỉ lệ khung", BlockPropGroups.Display, "1/1",
                Options:
                [
                    new BlockPropOption("1/1", "Vuông (1:1)"),
                    new BlockPropOption("4/3", "Ngang (4:3)"),
                    new BlockPropOption("3/4", "Dọc (3:4)"),
                    new BlockPropOption("16/9", "Rộng (16:9)")
                ],
                Hint: "Chỉ áp dụng khi bật \"Cắt ảnh vừa khung\"."),
            new BlockPropDescriptor("crop", BlockPropTypes.Checkbox, "Cắt ảnh vừa khung",
                BlockPropGroups.Display, "true",
                Hint: "Tắt = giữ nguyên tỉ lệ gốc từng ảnh (phù hợp masonry)."),
            new BlockPropDescriptor("folderId", BlockPropTypes.Select, "Thư mục ảnh", BlockPropGroups.Data,
                OptionsSource: BlockOptionSources.MediaFolders,
                Hint: "Không chọn = lấy toàn bộ thư viện ảnh."),
            new BlockPropDescriptor("includeSubfolders", BlockPropTypes.Checkbox, "Gồm cả thư mục con",
                BlockPropGroups.Data, "false"),
            SharedBlockDescriptors.Count("Số ảnh", DefaultCount, MaxCount),
            new BlockPropDescriptor("mediaIds", BlockPropTypes.ItemPicker, "Chọn ảnh cụ thể",
                BlockPropGroups.Data, ItemSource: BlockItemSources.Media,
                Hint: "Chọn tay và kéo để sắp thứ tự. Có danh sách này thì bỏ qua thư mục."),
            new BlockPropDescriptor("orderBy", BlockPropTypes.Select, "Sắp xếp", BlockPropGroups.Data, "newest",
                Options:
                [
                    new BlockPropOption("newest", "Mới tải lên trước"),
                    new BlockPropOption("oldest", "Cũ nhất trước"),
                    new BlockPropOption("name", "Theo tên tệp")
                ]),
            new BlockPropDescriptor("lightbox", BlockPropTypes.Checkbox, "Bấm để xem phóng to",
                BlockPropGroups.Content, "true"),
            new BlockPropDescriptor("showCaption", BlockPropTypes.Checkbox, "Hiện chú thích",
                BlockPropGroups.Content, "false",
                Hint: "Lấy từ Tiêu đề của ảnh, thiếu thì lấy Alt."),
            SharedBlockDescriptors.EmptyText(),
            SharedBlockDescriptors.Skip()
        ],
        DefaultPropsJson: $"{{\"count\":{DefaultCount}}}");

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var shared = new SharedBlockProps(props);
        var count = Math.Clamp(props.GetInt("count", DefaultCount), 1, MaxCount);
        var mediaIds = props.GetGuidArray("mediaIds");
        var crop = props.GetBool("crop", true);
        var ratio = NormalizeRatio(props.GetString("ratio"));
        var lightbox = props.GetBool("lightbox", true);
        var showCaption = props.GetBool("showCaption", false);

        var items = await LoadAsync(props, shared, mediaIds, count, ct);
        if (items.Count == 0) return shared.EmptyState("<!-- gallery: no media -->");

        var sb = new StringBuilder();
        // data-nc-gallery: móc cho lightbox; thiếu JS thì attribute này vô hại.
        sb.Append("<div class=\"nc-gallery\"");
        if (lightbox) sb.Append(" data-nc-gallery=\"1\"");
        sb.Append($" style=\"{shared.ContainerStyle(MinTileWidth, 16)}\">");

        foreach (var m in items) AppendTile(sb, m, shared, crop, ratio, lightbox, showCaption);

        sb.Append("</div>");

        // Nạp lightbox ngay trong HTML của khối: khối nằm ở đâu (trang public hay canvas
        // builder) thì script theo tới đó, không phải nhớ khai báo thêm ở layout.
        if (lightbox)
            sb.Append($"<script src=\"{WebUtility.HtmlEncode(BlockAssets.Versioned(_env, JsPath))}\" defer></script>");

        return sb.ToString();
    }

    /// <summary>
    /// Số ảnh khớp bộ lọc, bỏ qua count/skip. Chọn tay thì đếm chính số ảnh đã chọn còn tồn tại —
    /// người dùng cần biết ảnh nào đã bị xoá khỏi thư viện, không phải tổng số ảnh của site.
    /// </summary>
    public async Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var query = _db.Medias.AsNoTracking().Where(m => !m.IsDeleted && m.Kind == "image");

        var mediaIds = props.GetGuidArray("mediaIds");
        if (mediaIds.Count > 0)
            return await query.CountAsync(m => mediaIds.Contains(m.Id), ct);

        var folderIds = await ResolveFolderIdsAsync(props, ct);
        if (folderIds is not null)
            query = query.Where(m => m.FolderId != null && folderIds.Contains(m.FolderId.Value));

        return await query.CountAsync(ct);
    }

    private async Task<List<MediaRow>> LoadAsync(
        JsonProps props, SharedBlockProps shared, IReadOnlyList<Guid> mediaIds, int count, CancellationToken ct)
    {
        var query = _db.Medias.AsNoTracking().Where(m => !m.IsDeleted && m.Kind == "image");

        if (mediaIds.Count > 0)
        {
            // Chọn tay: lấy đúng những ảnh đó rồi sắp lại theo thứ tự người dùng kéo.
            var picked = await query
                .Where(m => mediaIds.Contains(m.Id))
                .Select(m => new MediaRow(m.Id, m.FilePath, m.Title, m.AltText, m.Width, m.Height))
                .ToListAsync(ct);

            var rank = new Dictionary<Guid, int>(mediaIds.Count);
            for (var i = 0; i < mediaIds.Count; i++) rank.TryAdd(mediaIds[i], i);
            return picked
                .OrderBy(m => rank.TryGetValue(m.Id, out var i) ? i : int.MaxValue)
                .Take(count)
                .ToList();
        }

        var folderIds = await ResolveFolderIdsAsync(props, ct);
        if (folderIds is not null)
            query = query.Where(m => m.FolderId != null && folderIds.Contains(m.FolderId.Value));

        query = props.GetString("orderBy") switch
        {
            "oldest" => query.OrderBy(m => m.CreatedAt),
            "name" => query.OrderBy(m => m.FileName),
            _ => query.OrderByDescending(m => m.CreatedAt)
        };

        if (shared.Skip > 0) query = query.Skip(shared.Skip);

        return await query
            .Take(count)
            .Select(m => new MediaRow(m.Id, m.FilePath, m.Title, m.AltText, m.Width, m.Height))
            .ToListAsync(ct);
    }

    /// <summary>
    /// null = không lọc theo thư mục (lấy cả thư viện). Ngược lại là tập thư mục được phép,
    /// đã mở rộng xuống con cháu nếu bật includeSubfolders.
    /// </summary>
    private async Task<List<Guid>?> ResolveFolderIdsAsync(JsonProps props, CancellationToken ct)
    {
        var raw = props.GetString("folderId");
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var folderId) || folderId == Guid.Empty)
            return null;

        var result = new List<Guid> { folderId };
        if (!props.GetBool("includeSubfolders", false)) return result;

        var edges = await _db.MediaFolders.AsNoTracking()
            .Where(f => f.ParentId != null)
            .Select(f => new { f.Id, ParentId = f.ParentId!.Value })
            .ToListAsync(ct);

        var childrenOf = edges.GroupBy(e => e.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var seen = new HashSet<Guid> { folderId };
        var queue = new Queue<Guid>(seen);
        while (queue.Count > 0)
        {
            if (!childrenOf.TryGetValue(queue.Dequeue(), out var kids)) continue;
            foreach (var kid in kids)
                if (seen.Add(kid)) queue.Enqueue(kid);
        }
        return seen.ToList();
    }

    private static void AppendTile(
        StringBuilder sb, MediaRow m, SharedBlockProps shared,
        bool crop, string ratio, bool lightbox, bool showCaption)
    {
        var caption = string.IsNullOrWhiteSpace(m.Title) ? m.Alt : m.Title;
        var alt = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(m.Alt) ? caption ?? "" : m.Alt);
        var src = WebUtility.HtmlEncode(m.FilePath);

        // width/height thật để trình duyệt giữ chỗ — không có thì rơi về tỉ lệ khung đã chọn.
        var size = m.Width is > 0 && m.Height is > 0 ? $" width=\"{m.Width}\" height=\"{m.Height}\"" : "";

        var figureStyle = $"margin:0;overflow:hidden;border-radius:var(--radius-card,12px)" +
                          shared.ItemExtraStyle(MinTileWidth);

        sb.Append($"<figure style=\"{figureStyle}\">");

        // Lightbox mở ảnh gốc bằng chính <a href> — không JS thì đây vẫn là link ảnh dùng được.
        if (lightbox) sb.Append($"<a href=\"{src}\" data-nc-lightbox target=\"_blank\" rel=\"noopener\" style=\"display:block\">");

        sb.Append($"<img src=\"{src}\" alt=\"{alt}\"{size} loading=\"lazy\" decoding=\"async\" style=\"");
        sb.Append(crop
            ? $"width:100%;aspect-ratio:{ratio};object-fit:cover;display:block"
            : "width:100%;height:auto;display:block");
        sb.Append("\">");

        if (lightbox) sb.Append("</a>");

        if (showCaption && !string.IsNullOrWhiteSpace(caption))
        {
            sb.Append("<figcaption style=\"padding:8px 2px 0;font-size:.85rem;color:var(--color-muted,#64748b)\">");
            sb.Append(WebUtility.HtmlEncode(caption));
            sb.Append("</figcaption>");
        }

        sb.Append("</figure>");
    }

    private static string NormalizeRatio(string? v) => v switch
    {
        "4/3" => "4/3",
        "3/4" => "3/4",
        "16/9" => "16/9",
        _ => "1/1"
    };

    private sealed record MediaRow(Guid Id, string FilePath, string? Title, string? Alt, int? Width, int? Height);
}
