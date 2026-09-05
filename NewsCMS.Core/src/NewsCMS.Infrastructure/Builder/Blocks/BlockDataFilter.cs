using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Bộ lọc nguồn dữ liệu DÙNG CHUNG cho mọi khối động lấy dữ liệu theo danh sách
/// (bài viết, sản phẩm, ảnh). Đọc một chỗ để 8 khối không lệch nhau về ý nghĩa từng prop.
///
/// Tương thích ngược là yêu cầu cứng: mọi trang đang chạy đều dùng <c>categorySlug</c> (số ít),
/// nên <see cref="CategorySlugs"/> đọc CẢ key số nhiều mới và key số ít cũ.
/// </summary>
internal sealed class BlockDataFilter
{
    public BlockDataFilter(JsonProps props)
    {
        var many = props.GetStringArray("categorySlugs");
        if (many.Count == 0)
        {
            // Key cũ: một chuyên mục, chuỗi rỗng = "tất cả".
            var single = props.GetString("categorySlug");
            many = string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single.Trim() };
        }
        CategorySlugs = many;

        IncludeChildCategories = props.GetBool("includeChildCategories", false);
        TagSlugs = props.GetStringArray("tagSlugs");
        FeaturedOnly = props.GetBool("featuredOnly", false);
        ItemIds = props.GetGuidArray("itemIds");
        ExcludeIds = props.GetGuidArray("excludeIds");
        PublishedWithinDays = Math.Clamp(props.GetInt("publishedWithinDays", 0), 0, 3650);
    }

    /// <summary>Rỗng = tất cả chuyên mục.</summary>
    public IReadOnlyList<string> CategorySlugs { get; }
    public bool IncludeChildCategories { get; }
    /// <summary>Rỗng = không lọc theo thẻ. Có nhiều thẻ = khớp BẤT KỲ (OR).</summary>
    public IReadOnlyList<string> TagSlugs { get; }
    public bool FeaturedOnly { get; }
    /// <summary>Chọn tay: có danh sách này thì bộ lọc chuyên mục/thẻ bị bỏ qua và thứ tự do người dùng sắp.</summary>
    public IReadOnlyList<Guid> ItemIds { get; }
    public IReadOnlyList<Guid> ExcludeIds { get; }
    /// <summary>0 = không giới hạn.</summary>
    public int PublishedWithinDays { get; }

    public bool HasManualSelection => ItemIds.Count > 0;

    /// <summary>Mốc thời gian sớm nhất được lấy, hoặc null nếu không giới hạn.</summary>
    public DateTime? PublishedSince =>
        PublishedWithinDays > 0 ? DateTime.UtcNow.AddDays(-PublishedWithinDays) : null;

    /// <summary>
    /// Sắp lại danh sách đã lấy về theo đúng thứ tự người dùng kéo trong item-picker.
    /// Phải làm sau khi query: SQL không giữ thứ tự của mệnh đề IN, nên nếu không sắp lại thì
    /// "chọn tay" chỉ có tác dụng lọc chứ không có tác dụng sắp — đúng vấn đề của
    /// <c>orderBy: "manual"</c> trước đây.
    /// </summary>
    public List<T> ApplyManualOrder<T>(List<T> rows, Func<T, Guid> idOf)
    {
        if (!HasManualSelection) return rows;
        var rank = new Dictionary<Guid, int>(ItemIds.Count);
        for (var i = 0; i < ItemIds.Count; i++) rank.TryAdd(ItemIds[i], i);
        return rows.OrderBy(r => rank.TryGetValue(idOf(r), out var i) ? i : int.MaxValue).ToList();
    }

    /// <summary>Các prop nguồn dữ liệu chuẩn cho khối lấy bài viết.</summary>
    public static IEnumerable<BlockPropDescriptor> PostProps(string countLabel, int defaultCount, int maxCount) =>
    [
        SharedBlockDescriptors.Count(countLabel, defaultCount, maxCount),
        SharedBlockDescriptors.CategorySlugs(BlockOptionSources.PostCategories),
        SharedBlockDescriptors.IncludeChildCategories(),
        SharedBlockDescriptors.TagSlugs(),
        SharedBlockDescriptors.FeaturedOnly("Chỉ bài nổi bật"),
        SharedBlockDescriptors.ItemIds(BlockItemSources.Post, "Chọn bài cụ thể"),
        SharedBlockDescriptors.OrderBy(),
        SharedBlockDescriptors.ExcludeIds(BlockItemSources.Post, "Loại trừ bài"),
        SharedBlockDescriptors.PublishedWithinDays(),
        SharedBlockDescriptors.Skip()
    ];

    /// <summary>Các prop nguồn dữ liệu chuẩn cho khối lấy sản phẩm (không có tag).</summary>
    public static IEnumerable<BlockPropDescriptor> ProductProps(string countLabel, int defaultCount, int maxCount) =>
    [
        SharedBlockDescriptors.Count(countLabel, defaultCount, maxCount),
        SharedBlockDescriptors.CategorySlugs(BlockOptionSources.ProductCategories, "Chuyên mục sản phẩm"),
        SharedBlockDescriptors.IncludeChildCategories(),
        SharedBlockDescriptors.FeaturedOnly("Chỉ hàng nổi bật"),
        SharedBlockDescriptors.ItemIds(BlockItemSources.Product, "Chọn sản phẩm cụ thể"),
        SharedBlockDescriptors.OrderBy(),
        SharedBlockDescriptors.ExcludeIds(BlockItemSources.Product, "Loại trừ sản phẩm"),
        SharedBlockDescriptors.Skip()
    ];
}
