using System.Net;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Đọc các prop KIỂU HIỂN THỊ dùng chung cho mọi khối động:
/// orderBy / skip / layout / columns / gap / showExcerpt / showDate / showAuthor / emptyText.
///
/// Mỗi <see cref="NewsCMS.Application.Builder.IDynamicBlock"/> tự quyết định áp dụng prop nào cho
/// hợp lý (vd category-list không có excerpt), nhưng việc ĐỌC + chuẩn hoá giá trị nằm một chỗ để
/// các khối không lệch nhau. Bọc quanh <see cref="JsonProps"/> — vẫn không throw khi thiếu/malformed.
///
/// Quy tắc bố cục: mọi layout phải thành hình bằng CSS THUẦN, không cần JS. Bố cục do JS quyết định
/// là nguyên nhân lỗi "canvas vỡ, ảnh khổng lồ đè cả trang" (lệch realm giữa trang admin và iframe)
/// và luôn mong manh trong WYSIWYG. Vì thế masonry dùng <c>column-count</c> chứ không phải Masonry.js.
/// </summary>
internal sealed class SharedBlockProps
{
    private readonly JsonProps _props;

    public SharedBlockProps(JsonProps props)
    {
        _props = props;
        HasExplicitOrder = props.Has("orderBy");
        OrderBy = NormalizeOrder(props.GetString("orderBy"));
        Skip = Math.Clamp(props.GetInt("skip", 0), 0, 500);
        Layout = NormalizeLayout(props.GetString("layout"));
        Columns = Math.Clamp(props.GetInt("columns", 0), 0, 6);
        Gap = props.Has("gap") ? Math.Clamp(props.GetInt("gap", 0), 0, 64) : null;
        ShowExcerpt = props.GetBool("showExcerpt", true);
        ShowDate = props.GetBool("showDate", true);
        ShowAuthor = props.GetBool("showAuthor", false);
        var t = props.GetString("emptyText");
        EmptyText = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }

    /// <summary>"newest" | "most-viewed" | "manual".</summary>
    public string OrderBy { get; }

    /// <summary>
    /// true nếu người dùng thực sự đặt <c>orderBy</c>. Cần cho khối có mặc định KHÁC "newest"
    /// (coffee-bean-grid mặc định theo SortOrder) — không có cờ này thì không phân biệt được
    /// "chưa chọn" với "chọn newest".
    /// </summary>
    public bool HasExplicitOrder { get; }
    public int Skip { get; }
    /// <summary>"grid" | "list" | "carousel" | "masonry" | "featured".</summary>
    public string Layout { get; }
    /// <summary>0 = tự động (auto-fit theo minWidth); &gt;0 = số cột cố định.</summary>
    public int Columns { get; }
    /// <summary>null = dùng mặc định của khối.</summary>
    public int? Gap { get; }
    public bool ShowExcerpt { get; }
    public bool ShowDate { get; }
    public bool ShowAuthor { get; }
    public string? EmptyText { get; }

    /// <summary>Bố cục "1 lớn + còn lại nhỏ" — khối phải tự làm nổi mục đầu tiên.</summary>
    public bool IsFeaturedLayout => Layout == "featured";

    private static string NormalizeOrder(string? v) => v switch
    {
        "most-viewed" => "most-viewed",
        "manual" => "manual",
        _ => "newest"
    };

    private static string NormalizeLayout(string? v) => v switch
    {
        "list" => "list",
        "carousel" => "carousel",
        "masonry" => "masonry",
        "featured" => "featured",
        _ => "grid"
    };

    /// <summary>
    /// Sinh style cho container theo layout/columns đã chọn:
    /// list → cột dọc; carousel → hàng ngang cuộn snap; masonry → column-count (CSS thuần);
    /// featured → lưới 2 cột, mục đầu chiếm hết hàng đầu; grid + columns → số cột cố định;
    /// grid mặc định → auto-fit theo <paramref name="minWidth"/>.
    /// </summary>
    public string ContainerStyle(int minWidth, int gap = 20)
    {
        var g = Gap ?? gap;
        return Layout switch
        {
            "list" => $"display:flex;flex-direction:column;gap:{g}px",
            "carousel" => $"display:flex;gap:{g}px;overflow-x:auto;scroll-snap-type:x mandatory;-webkit-overflow-scrolling:touch;padding-bottom:4px",
            // column-count: bố cục so le không cần JS. column-gap là khoảng ngang; khoảng dọc do
            // margin-bottom của từng item (xem ItemExtraStyle) vì column layout không có row-gap.
            "masonry" => $"column-count:{(Columns > 0 ? Columns : 3)};column-gap:{g}px",
            // featured: 2 cột cho desktop, mục đầu span cả 2 (đặt ở ItemExtraStyle của khối).
            "featured" => $"display:grid;grid-template-columns:repeat(auto-fit,minmax({minWidth}px,1fr));gap:{g}px",
            _ when Columns > 0 => $"display:grid;grid-template-columns:repeat({Columns},minmax(0,1fr));gap:{g}px",
            _ => $"display:grid;grid-template-columns:repeat(auto-fit,minmax({minWidth}px,1fr));gap:{g}px"
        };
    }

    /// <summary>
    /// Style thêm cho mỗi card theo layout: carousel giữ bề rộng + snap; masonry cần
    /// <c>break-inside:avoid</c> để card không bị cắt giữa hai cột.
    /// </summary>
    public string ItemExtraStyle(int minWidth, bool isFirst = false)
    {
        var g = Gap ?? 20;
        return Layout switch
        {
            "carousel" => $";flex:0 0 {minWidth}px;scroll-snap-align:start",
            "masonry" => $";break-inside:avoid;-webkit-column-break-inside:avoid;margin-bottom:{g}px",
            "featured" when isFirst => ";grid-column:1/-1",
            _ => ""
        };
    }

    /// <summary>
    /// HTML trạng thái rỗng: nếu người dùng đặt emptyText thì hiện khối thông báo nhìn thấy được
    /// (thay vì comment HTML vô hình khiến builder trông như hỏng); không đặt thì trả comment gọn
    /// để không chiếm chỗ trên trang public.
    /// </summary>
    public string EmptyState(string fallbackComment)
    {
        if (string.IsNullOrWhiteSpace(EmptyText)) return fallbackComment;
        return "<div data-nc-part=\"empty\" style=\"padding:32px 20px;text-align:center;color:var(--color-muted,#94a3b8);" +
               "font-family:var(--font-body,system-ui,sans-serif);font-size:.95rem\">" +
               WebUtility.HtmlEncode(EmptyText) + "</div>";
    }
}
