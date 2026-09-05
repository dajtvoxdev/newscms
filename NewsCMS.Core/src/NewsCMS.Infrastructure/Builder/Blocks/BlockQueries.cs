using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Áp <see cref="BlockDataFilter"/> lên IQueryable của Post/Product. Ba khối bài viết và hai khối
/// sản phẩm dùng chung hàm ở đây, nên "gồm chuyên mục con" hay "chọn tay" hoạt động y hệt nhau ở
/// mọi khối thay vì mỗi khối tự viết một kiểu.
/// </summary>
internal static class BlockQueries
{
    /// <summary>Bài đã publish, chưa xoá — điểm bắt đầu chung.</summary>
    public static IQueryable<Post> PublishedPosts(AppDbContext db) => db.Posts.AsNoTracking()
        .Where(p => p.Status == PostStatus.Published && !p.IsDeleted);

    /// <summary>Sản phẩm đã publish, chưa xoá.</summary>
    public static IQueryable<Product> PublishedProducts(AppDbContext db) => db.Products.AsNoTracking()
        .Where(p => p.Status == ProductStatus.Published && !p.IsDeleted);

    /// <summary>
    /// Lọc bài viết theo filter. Chọn tay (<c>itemIds</c>) THAY THẾ bộ lọc chuyên mục/thẻ/nổi bật —
    /// nếu vừa lọc vừa chọn tay thì người dùng chọn một bài ngoài chuyên mục là nó âm thầm biến mất,
    /// khó hiểu hơn nhiều so với "đã chọn tay thì lấy đúng những gì đã chọn".
    /// </summary>
    public static async Task<IQueryable<Post>> ApplyAsync(
        IQueryable<Post> query, AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        if (filter.HasManualSelection)
            return query.Where(p => filter.ItemIds.Contains(p.Id));

        if (filter.CategorySlugs.Count > 0)
        {
            var ids = await ResolvePostCategoryIdsAsync(db, filter, ct);
            query = query.Where(p => ids.Contains(p.CategoryId));
        }

        if (filter.TagSlugs.Count > 0)
            query = query.Where(p => p.PostTags.Any(pt => filter.TagSlugs.Contains(pt.Tag.Slug)));

        if (filter.FeaturedOnly)
            query = query.Where(p => p.IsFeatured);

        if (filter.PublishedSince is { } since)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= since);

        if (filter.ExcludeIds.Count > 0)
            query = query.Where(p => !filter.ExcludeIds.Contains(p.Id));

        return query;
    }

    /// <summary>Lọc sản phẩm theo filter. Sản phẩm không có tag nên bỏ qua tagSlugs.</summary>
    public static async Task<IQueryable<Product>> ApplyAsync(
        IQueryable<Product> query, AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        if (filter.HasManualSelection)
            return query.Where(p => filter.ItemIds.Contains(p.Id));

        if (filter.CategorySlugs.Count > 0)
        {
            var ids = await ResolveProductCategoryIdsAsync(db, filter, ct);
            query = query.Where(p => ids.Contains(p.ProductCategoryId));
        }

        if (filter.FeaturedOnly)
            query = query.Where(p => p.IsFeatured);

        if (filter.PublishedSince is { } since)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= since);

        if (filter.ExcludeIds.Count > 0)
            query = query.Where(p => !filter.ExcludeIds.Contains(p.Id));

        return query;
    }

    /// <summary>
    /// Sắp thứ tự bài viết. Chọn tay thì KHÔNG sắp ở SQL (thứ tự do
    /// <see cref="BlockDataFilter.ApplyManualOrder"/> quyết định sau khi lấy về), nhưng vẫn cần một
    /// OrderBy ổn định để Skip/Take không trả kết quả ngẫu nhiên.
    /// </summary>
    public static IQueryable<Post> Order(IQueryable<Post> query, SharedBlockProps shared, BlockDataFilter filter)
    {
        if (filter.HasManualSelection)
            return query.OrderBy(p => p.Id);

        return shared.OrderBy switch
        {
            "most-viewed" => query.OrderByDescending(p => p.ViewCount).ThenByDescending(p => p.PublishedAt ?? p.CreatedAt),
            "manual" => query.OrderByDescending(p => p.IsFeatured).ThenByDescending(p => p.PublishedAt ?? p.CreatedAt),
            _ => query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
        };
    }

    /// <summary>
    /// Sắp thứ tự sản phẩm; "manual" = theo SortOrder do người dùng đặt ở trang sản phẩm.
    /// <paramref name="fallbackOrder"/> cho khối có mặc định khác "newest" (vd coffee-bean-grid
    /// vốn xếp theo SortOrder) — chỉ dùng khi người dùng CHƯA chọn kiểu sắp nào.
    /// </summary>
    public static IQueryable<Product> Order(
        IQueryable<Product> query, SharedBlockProps shared, BlockDataFilter filter, string? fallbackOrder = null)
    {
        if (filter.HasManualSelection)
            return query.OrderBy(p => p.Id);

        var order = !shared.HasExplicitOrder && fallbackOrder is not null ? fallbackOrder : shared.OrderBy;
        return order switch
        {
            "most-viewed" => query.OrderByDescending(p => p.ViewCount).ThenByDescending(p => p.PublishedAt),
            "manual" => query.OrderBy(p => p.SortOrder).ThenByDescending(p => p.PublishedAt),
            _ => query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
        };
    }

    /// <summary>
    /// Số bài KHỚP bộ lọc, bỏ qua count/skip — cho panel hiện "Khớp 12 bài, hiển thị 6".
    /// Chọn tay thì đếm chính số đã chọn còn tồn tại, không phải số đã publish nói chung.
    /// </summary>
    public static async Task<int> CountPostsAsync(
        AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        var query = await ApplyAsync(PublishedPosts(db), db, filter, ct);
        return await query.CountAsync(ct);
    }

    /// <summary>Số sản phẩm khớp bộ lọc, bỏ qua count/skip.</summary>
    public static async Task<int> CountProductsAsync(
        AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        var query = await ApplyAsync(PublishedProducts(db), db, filter, ct);
        return await query.CountAsync(ct);
    }

    /// <summary>
    /// Slug chuyên mục bài viết → Id, mở rộng xuống con cháu khi bật includeChildCategories.
    /// Dùng Id chứ không so slug trực tiếp vì cây chuyên mục cần duyệt nhiều tầng.
    /// </summary>
    private static async Task<List<Guid>> ResolvePostCategoryIdsAsync(
        AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        var slugs = filter.CategorySlugs;
        var roots = await db.Categories.AsNoTracking()
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (!filter.IncludeChildCategories || roots.Count == 0) return roots;

        var edges = await db.Categories.AsNoTracking()
            .Where(c => c.ParentId != null)
            .Select(c => new { c.Id, ParentId = c.ParentId!.Value })
            .ToListAsync(ct);

        return ExpandTree(roots, edges.Select(e => (e.Id, e.ParentId)));
    }

    private static async Task<List<Guid>> ResolveProductCategoryIdsAsync(
        AppDbContext db, BlockDataFilter filter, CancellationToken ct)
    {
        var slugs = filter.CategorySlugs;
        var roots = await db.ProductCategories.AsNoTracking()
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (!filter.IncludeChildCategories || roots.Count == 0) return roots;

        var edges = await db.ProductCategories.AsNoTracking()
            .Where(c => c.ParentId != null && !c.IsDeleted)
            .Select(c => new { c.Id, ParentId = c.ParentId!.Value })
            .ToListAsync(ct);

        return ExpandTree(roots, edges.Select(e => (e.Id, e.ParentId)));
    }

    /// <summary>
    /// Mở rộng tập gốc xuống toàn bộ con cháu bằng BFS trên danh sách cạnh (child → parent).
    /// Duyệt trên bộ nhớ vì cây chuyên mục nhỏ; đệ quy trong SQL sẽ cần raw CTE, không đáng.
    /// Node đã thăm được bỏ qua nên dữ liệu có vòng lặp cũng không treo.
    /// </summary>
    private static List<Guid> ExpandTree(List<Guid> roots, IEnumerable<(Guid Id, Guid ParentId)> edges)
    {
        var childrenOf = edges
            .GroupBy(e => e.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var result = new HashSet<Guid>(roots);
        var queue = new Queue<Guid>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenOf.TryGetValue(current, out var kids)) continue;
            foreach (var kid in kids)
                if (result.Add(kid)) queue.Enqueue(kid);
        }
        return result.ToList();
    }
}
