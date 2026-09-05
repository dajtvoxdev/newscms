using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Mô tả (descriptor) cho các prop DÙNG CHUNG giữa nhiều khối động, khớp 1-1 với
/// <see cref="SharedBlockProps"/> (kiểu hiển thị) và <see cref="BlockDataFilter"/> (nguồn dữ liệu).
///
/// Khối chỉ việc gọi <c>Display(...)</c> / <c>Filter(...)</c> rồi ghép thêm prop riêng — nhãn,
/// khoảng min/max và giá trị mặc định vì thế không thể lệch nhau giữa các khối như trước.
/// </summary>
internal static class SharedBlockDescriptors
{
    // ── Kiểu hiển thị ────────────────────────────────────────────────────────────

    public static BlockPropDescriptor Layout() => new(
        "layout", BlockPropTypes.Select, "Bố cục", BlockPropGroups.Display, "grid",
        Options: new[]
        {
            new BlockPropOption("grid", "Lưới"),
            new BlockPropOption("list", "Danh sách dọc"),
            new BlockPropOption("carousel", "Cuộn ngang"),
            new BlockPropOption("masonry", "Masonry (so le)"),
            new BlockPropOption("featured", "1 lớn + còn lại nhỏ")
        });

    public static BlockPropDescriptor Columns() => new(
        "columns", BlockPropTypes.Number, "Số cột (0 = tự động)", BlockPropGroups.Display, "0",
        Min: 0, Max: 6);

    public static BlockPropDescriptor Gap() => new(
        "gap", BlockPropTypes.Number, "Khoảng cách (px)", BlockPropGroups.Display, null,
        Min: 0, Max: 64, Hint: "Để trống = theo mặc định của khối.");

    public static BlockPropDescriptor OrderBy() => new(
        "orderBy", BlockPropTypes.Select, "Sắp xếp", BlockPropGroups.Data, "newest",
        Options: new[]
        {
            new BlockPropOption("newest", "Mới nhất"),
            new BlockPropOption("most-viewed", "Xem nhiều"),
            new BlockPropOption("manual", "Thủ công (theo danh sách chọn tay)")
        });

    public static BlockPropDescriptor Skip() => new(
        "skip", BlockPropTypes.Number, "Bỏ qua (skip)", BlockPropGroups.Advanced, "0",
        Min: 0, Max: 500, Hint: "Bỏ qua N mục đầu — dùng khi hai khối cùng nguồn không được lặp nhau.");

    public static BlockPropDescriptor ShowExcerpt() => new(
        "showExcerpt", BlockPropTypes.Checkbox, "Hiện mô tả ngắn", BlockPropGroups.Content, "true");

    public static BlockPropDescriptor ShowDate() => new(
        "showDate", BlockPropTypes.Checkbox, "Hiện ngày đăng", BlockPropGroups.Content, "true");

    public static BlockPropDescriptor ShowAuthor() => new(
        "showAuthor", BlockPropTypes.Checkbox, "Hiện tác giả", BlockPropGroups.Content, "false");

    public static BlockPropDescriptor EmptyText() => new(
        "emptyText", BlockPropTypes.Text, "Chữ khi không có dữ liệu", BlockPropGroups.Content,
        Placeholder: "vd. Chưa có bài viết nào",
        Hint: "Để trống thì khối tự ẩn trên trang public.");

    // ── Nguồn dữ liệu (BlockDataFilter) ──────────────────────────────────────────

    public static BlockPropDescriptor Count(string label, int @default, int max) => new(
        "count", BlockPropTypes.Number, label, BlockPropGroups.Data, @default.ToString(),
        Min: 1, Max: max);

    public static BlockPropDescriptor CategorySlugs(string optionsSource, string label = "Chuyên mục") => new(
        "categorySlugs", BlockPropTypes.MultiSelect, label, BlockPropGroups.Data,
        OptionsSource: optionsSource,
        Hint: "Không chọn gì = lấy tất cả chuyên mục.");

    public static BlockPropDescriptor IncludeChildCategories() => new(
        "includeChildCategories", BlockPropTypes.Checkbox, "Gồm cả chuyên mục con",
        BlockPropGroups.Data, "false");

    public static BlockPropDescriptor TagSlugs() => new(
        "tagSlugs", BlockPropTypes.MultiSelect, "Thẻ (tag)", BlockPropGroups.Data,
        OptionsSource: BlockOptionSources.Tags,
        Hint: "Bài phải có ÍT NHẤT một trong các thẻ đã chọn.");

    public static BlockPropDescriptor FeaturedOnly(string label = "Chỉ mục nổi bật") => new(
        "featuredOnly", BlockPropTypes.Checkbox, label, BlockPropGroups.Data, "false");

    public static BlockPropDescriptor ItemIds(string itemSource, string label) => new(
        "itemIds", BlockPropTypes.ItemPicker, label, BlockPropGroups.Data,
        ItemSource: itemSource,
        Hint: "Chọn tay và kéo để sắp thứ tự. Có danh sách này thì bộ lọc chuyên mục/thẻ bị bỏ qua.");

    public static BlockPropDescriptor ExcludeIds(string itemSource, string label = "Loại trừ") => new(
        "excludeIds", BlockPropTypes.ItemPicker, label, BlockPropGroups.Advanced,
        ItemSource: itemSource,
        Hint: "Không hiển thị các mục này — vd tránh lặp bài đang xem.");

    public static BlockPropDescriptor PublishedWithinDays() => new(
        "publishedWithinDays", BlockPropTypes.Number, "Chỉ lấy trong N ngày gần đây",
        BlockPropGroups.Advanced, "0", Min: 0, Max: 3650,
        Hint: "0 = không giới hạn thời gian.");

    // ── Thumbnail SVG cho preset ─────────────────────────────────────────────────

    /// <summary>Thumbnail lưới n cột × 2 hàng.</summary>
    public static string GridThumb(int cols)
    {
        var w = (48.0 - (cols - 1) * 3) / cols;
        var sb = new System.Text.StringBuilder("<svg viewBox=\"0 0 48 32\">");
        for (var r = 0; r < 2; r++)
            for (var c = 0; c < cols; c++)
                sb.Append($"<rect x=\"{c * (w + 3):0.##}\" y=\"{r * 17}\" width=\"{w:0.##}\" height=\"14\" rx=\"2\"/>");
        return sb.Append("</svg>").ToString();
    }

    public const string ListThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"0\" width=\"16\" height=\"14\" rx=\"2\"/>" +
        "<rect x=\"19\" y=\"2\" width=\"29\" height=\"4\" rx=\"2\"/><rect x=\"19\" y=\"9\" width=\"22\" height=\"3\" rx=\"1.5\"/>" +
        "<rect x=\"0\" y=\"18\" width=\"16\" height=\"14\" rx=\"2\"/>" +
        "<rect x=\"19\" y=\"20\" width=\"29\" height=\"4\" rx=\"2\"/><rect x=\"19\" y=\"27\" width=\"22\" height=\"3\" rx=\"1.5\"/></svg>";

    public const string CarouselThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"6\" width=\"20\" height=\"20\" rx=\"2\"/>" +
        "<rect x=\"23\" y=\"6\" width=\"20\" height=\"20\" rx=\"2\"/><rect x=\"46\" y=\"6\" width=\"2\" height=\"20\" rx=\"1\"/></svg>";

    public const string MasonryThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"0\" width=\"14\" height=\"18\" rx=\"2\"/>" +
        "<rect x=\"0\" y=\"21\" width=\"14\" height=\"11\" rx=\"2\"/><rect x=\"17\" y=\"0\" width=\"14\" height=\"11\" rx=\"2\"/>" +
        "<rect x=\"17\" y=\"14\" width=\"14\" height=\"18\" rx=\"2\"/><rect x=\"34\" y=\"0\" width=\"14\" height=\"20\" rx=\"2\"/>" +
        "<rect x=\"34\" y=\"23\" width=\"14\" height=\"9\" rx=\"2\"/></svg>";

    public const string FeaturedThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"0\" width=\"28\" height=\"32\" rx=\"2\"/>" +
        "<rect x=\"31\" y=\"0\" width=\"17\" height=\"7\" rx=\"2\"/><rect x=\"31\" y=\"8.5\" width=\"17\" height=\"7\" rx=\"2\"/>" +
        "<rect x=\"31\" y=\"17\" width=\"17\" height=\"7\" rx=\"2\"/><rect x=\"31\" y=\"25\" width=\"17\" height=\"7\" rx=\"2\"/></svg>";

    public const string TitleOnlyThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"3\" width=\"48\" height=\"4\" rx=\"2\"/>" +
        "<rect x=\"0\" y=\"11\" width=\"40\" height=\"4\" rx=\"2\"/><rect x=\"0\" y=\"19\" width=\"44\" height=\"4\" rx=\"2\"/>" +
        "<rect x=\"0\" y=\"27\" width=\"34\" height=\"4\" rx=\"2\"/></svg>";

    public const string ChipsThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"10\" width=\"13\" height=\"9\" rx=\"4.5\"/>" +
        "<rect x=\"16\" y=\"10\" width=\"16\" height=\"9\" rx=\"4.5\"/><rect x=\"35\" y=\"10\" width=\"11\" height=\"9\" rx=\"4.5\"/></svg>";

    public const string SquareGridThumb =
        "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"0\" width=\"15\" height=\"15\" rx=\"2\"/>" +
        "<rect x=\"16.5\" y=\"0\" width=\"15\" height=\"15\" rx=\"2\"/><rect x=\"33\" y=\"0\" width=\"15\" height=\"15\" rx=\"2\"/>" +
        "<rect x=\"0\" y=\"17\" width=\"15\" height=\"15\" rx=\"2\"/><rect x=\"16.5\" y=\"17\" width=\"15\" height=\"15\" rx=\"2\"/>" +
        "<rect x=\"33\" y=\"17\" width=\"15\" height=\"15\" rx=\"2\"/></svg>";
}
