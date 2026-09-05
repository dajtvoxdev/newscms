using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Preset "kiểu hiển thị" dùng chung cho các khối danh sách bài viết (post-list, news-grid).
/// Chọn preset = set một lượt vài prop; người dùng vẫn chỉnh tay từng prop sau đó.
///
/// Preset chỉ đặt prop, KHÔNG sinh class CSS mới: các khối phát inline style nên preset không thể
/// rơi ra ngoài safelist của site-utilities.css.
/// </summary>
internal static class PostBlockPresets
{
    public static IReadOnlyList<BlockPresetDescriptor> For(int defaultCount) =>
    [
        new("grid-3", "Lưới 3 cột", SharedBlockDescriptors.GridThumb(3),
            $"{{\"layout\":\"grid\",\"columns\":3,\"count\":{Math.Max(3, defaultCount)},\"showExcerpt\":true}}"),
        new("grid-4", "Lưới 4 cột", SharedBlockDescriptors.GridThumb(4),
            "{\"layout\":\"grid\",\"columns\":4,\"count\":8,\"showExcerpt\":false}"),
        new("list-h", "Danh sách ngang (ảnh trái)", SharedBlockDescriptors.ListThumb,
            "{\"layout\":\"list\",\"columns\":0,\"showExcerpt\":true}"),
        new("carousel", "Cuộn ngang", SharedBlockDescriptors.CarouselThumb,
            "{\"layout\":\"carousel\",\"columns\":0,\"count\":8}"),
        new("featured-hero", "1 bài lớn + 4 bài nhỏ", SharedBlockDescriptors.FeaturedThumb,
            "{\"layout\":\"featured\",\"columns\":0,\"count\":5,\"showExcerpt\":true}"),
        new("title-only", "Danh sách chỉ tiêu đề", SharedBlockDescriptors.TitleOnlyThumb,
            "{\"layout\":\"list\",\"columns\":0,\"showExcerpt\":false,\"showDate\":false}")
    ];
}

/// <summary>Preset cho các khối lưới sản phẩm (product-grid, coffee-bean-grid).</summary>
internal static class ProductBlockPresets
{
    public static IReadOnlyList<BlockPresetDescriptor> For(int defaultCount) =>
    [
        new("grid-4", "Lưới 4 cột", SharedBlockDescriptors.GridThumb(4),
            $"{{\"layout\":\"grid\",\"columns\":4,\"count\":{Math.Max(4, defaultCount)}}}"),
        new("grid-3-large", "Lưới 3 cột ảnh lớn", SharedBlockDescriptors.GridThumb(3),
            "{\"layout\":\"grid\",\"columns\":3,\"count\":6,\"showExcerpt\":true}"),
        new("carousel", "Cuộn ngang", SharedBlockDescriptors.CarouselThumb,
            "{\"layout\":\"carousel\",\"columns\":0,\"count\":8}"),
        new("masonry", "Masonry (so le)", SharedBlockDescriptors.MasonryThumb,
            "{\"layout\":\"masonry\",\"columns\":3,\"count\":9}")
    ];
}
