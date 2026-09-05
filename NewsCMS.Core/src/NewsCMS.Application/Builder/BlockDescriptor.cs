namespace NewsCMS.Application.Builder;

/// <summary>
/// Khối động TỰ MÔ TẢ mình: nhãn, nhóm, icon, các preset "kiểu hiển thị" và toàn bộ prop.
///
/// Vì sao cần: trước đây catalog khối khai báo HAI nơi — <c>DynamicBlockRegistry</c> phía server và
/// <c>dynamicBlockDefs()</c> trong nc-builder-extensions.js. Thêm khối mà quên sửa file JS là khối
/// biến mất khỏi builder (không preview, không trait, không kéo-thả được) mà không có lỗi nào báo.
/// Nay builder dựng palette + panel cấu hình TỪ dữ liệu này, và MCP (<c>site_list_blocks</c>) sinh
/// PropsSchemaJson cũng từ đây — một nguồn sự thật duy nhất.
/// </summary>
/// <param name="Label">Tên hiển thị trong palette, vd "Danh sách bài viết".</param>
/// <param name="Category">Nhóm trong palette: "Dữ liệu" | "Bố cục" | "Nội dung".</param>
/// <param name="Description">Một câu mô tả khối làm gì.</param>
/// <param name="IconSvg">SVG inline (đã là markup hoàn chỉnh <c>&lt;svg&gt;…&lt;/svg&gt;</c>).</param>
/// <param name="Presets">Các "kiểu hiển thị" đặt tên sẵn; rỗng = khối không có preset.</param>
/// <param name="Props">Mọi prop cấu hình được, kèm nhóm để panel xếp cho gọn.</param>
/// <param name="DefaultPropsJson">
/// Props gán sẵn khi kéo khối vào canvas, dạng JSON literal (vd <c>{"count":6}</c>).
/// Chỉ ghi những giá trị KHÁC mặc định của server để <c>data-nc-props</c> không phình.
/// </param>
/// <param name="EntityScoped">
/// Khối đọc <see cref="DynamicBlockContext.Route"/>, tức kết quả render KHÁC NHAU giữa hai bài
/// viết dù props y hệt. <c>DynamicBlockRenderer</c> nối EntityId vào cache key cho đúng những
/// khối này — bỏ sót là mọi bài hiện nội dung của bài render đầu tiên, và chỉ lộ ra sau khi
/// cache hết hạn 5 phút. Khối không đọc entity giữ nguyên <c>false</c> để vẫn dùng chung một
/// cache entry cho mọi URL (post-list, site-menu... trong cùng một template).
/// </param>
/// <param name="SupportedTypes">
/// Key của các loại nội dung (<see cref="IContentType.Key"/>) mà khối này dùng được — vd
/// <c>["product"]</c> cho khối giá/tồn kho. <c>null</c> = hợp với mọi loại.
/// Dùng để ẩn khối khỏi palette khi đang mở template sai loại: kéo "Giá sản phẩm" vào template
/// bài viết thì khối chỉ im lặng trả rỗng, người dùng không biết vì sao.
/// </param>
public sealed record BlockDescriptor(
    string Label,
    string Category,
    string Description,
    string IconSvg,
    IReadOnlyList<BlockPresetDescriptor> Presets,
    IReadOnlyList<BlockPropDescriptor> Props,
    string DefaultPropsJson = "{}",
    bool EntityScoped = false,
    IReadOnlyList<string>? SupportedTypes = null);

/// <summary>
/// Một "kiểu hiển thị" đặt tên sẵn: chọn preset = set một lượt các prop trong <paramref name="PropsJson"/>,
/// người dùng vẫn chỉnh tay từng prop sau đó. Đây là cách trả lời câu "muốn nhìn như thế nào" mà
/// không buộc người dùng tự mường tượng kết quả từ <c>layout</c> + <c>columns</c> thô.
/// </summary>
/// <param name="ThumbSvg">Hình khối nhỏ minh hoạ bố cục (SVG inline), hiện trong lưới chọn preset.</param>
public sealed record BlockPresetDescriptor(
    string Key,
    string Label,
    string ThumbSvg,
    string PropsJson);

/// <summary>
/// Mô tả một prop của khối: đủ để builder dựng ô nhập, và đủ để sinh JSON Schema cho agent MCP.
/// </summary>
/// <param name="Type">Xem <see cref="BlockPropTypes"/>.</param>
/// <param name="Group">Xem <see cref="BlockPropGroups"/> — panel xếp prop theo nhóm này.</param>
/// <param name="DefaultValue">
/// Mặc định dạng chuỗi ("6", "true", "grid"). Chuỗi vì prop có nhiều kiểu mà JSON attribute phải
/// giữ nguyên văn; client tự đổi kiểu theo <paramref name="Type"/>. Trùng mặc định thì prop được
/// lược khỏi <c>data-nc-props</c> — server tự áp lại đúng giá trị đó.
/// </param>
/// <param name="Options">Lựa chọn tĩnh cho select/multi-select.</param>
/// <param name="OptionsSource">
/// Nguồn lựa chọn ĐỘNG, do endpoint catalog điền lúc chạy (xem <see cref="BlockOptionSources"/>).
/// Nhờ vậy khối không cần biết gì về DbContext để mô tả chính nó.
/// </param>
/// <param name="ItemSource">Loại bản ghi cho item-picker (xem <see cref="BlockItemSources"/>).</param>
public sealed record BlockPropDescriptor(
    string Name,
    string Type,
    string Label,
    string Group = BlockPropGroups.Data,
    string? DefaultValue = null,
    IReadOnlyList<BlockPropOption>? Options = null,
    string? OptionsSource = null,
    string? ItemSource = null,
    int? Min = null,
    int? Max = null,
    string? Placeholder = null,
    string? Hint = null);

/// <summary>Một lựa chọn của select/multi-select.</summary>
public sealed record BlockPropOption(string Value, string Name);

/// <summary>Kiểu ô nhập builder biết dựng. Client map sang trait type tương ứng.</summary>
public static class BlockPropTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Checkbox = "checkbox";
    public const string Select = "select";
    /// <summary>Nhiều lựa chọn dạng chip; giá trị lưu là mảng chuỗi.</summary>
    public const string MultiSelect = "multi-select";
    /// <summary>Ô tìm kiếm + danh sách đã chọn (kéo sắp thứ tự); giá trị lưu là mảng Guid.</summary>
    public const string ItemPicker = "item-picker";
}

/// <summary>
/// Nhóm prop, quyết định thứ tự các mục trong panel: kiểu hiển thị → nguồn dữ liệu →
/// hiện những gì → nâng cao. Người dùng gần như luôn đi theo đúng trình tự đó.
/// </summary>
public static class BlockPropGroups
{
    public const string Display = "display";
    public const string Data = "data";
    public const string Content = "content";
    public const string Advanced = "advanced";
}

/// <summary>Khoá nguồn lựa chọn động; endpoint catalog dịch thành danh sách thật.</summary>
public static class BlockOptionSources
{
    public const string PostCategories = "post-categories";
    public const string ProductCategories = "product-categories";
    public const string BannerPositions = "banner-positions";
    public const string Tags = "tags";
    public const string MediaFolders = "media-folders";
    /// <summary>Các giá trị Location đang có menu (header/footer/…) — quản lý ở Admin → Menu.</summary>
    public const string MenuLocations = "menu-locations";
}

/// <summary>Loại bản ghi cho item-picker; khớp tham số <c>type</c> của endpoint picker items.</summary>
public static class BlockItemSources
{
    public const string Post = "post";
    public const string Product = "product";
    public const string Media = "media";
}
