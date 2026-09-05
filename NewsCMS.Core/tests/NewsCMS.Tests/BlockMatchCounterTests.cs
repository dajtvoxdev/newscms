using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// <see cref="IBlockMatchCounter"/> đứng sau dòng "Khớp N …, hiển thị M" trên panel.
/// Hai điều phải đúng:
///   • Đếm BỎ QUA count/skip — nó trả tổng số bản ghi khớp bộ lọc, không phải số đang hiển thị.
///   • Vẫn tôn trọng bộ lọc, gồm key số ít cũ <c>categorySlug</c> (tương thích ngược là bắt buộc).
/// </summary>
public sealed class BlockMatchCounterTests
{
    [Fact]
    public async Task CountMatches_IgnoresCount_ButRespectsCategoryFilter()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");
        var other = SeedCategory(db, "do-uong", "Đồ uống");

        // 3 sản phẩm khớp chuyên mục, 1 ngoài chuyên mục.
        db.Products.AddRange(
            NewProduct("A", beans.Id),
            NewProduct("B", beans.Id),
            NewProduct("C", beans.Id),
            NewProduct("D", other.Id));
        await db.SaveChangesAsync();

        var counter = (IBlockMatchCounter)new CoffeeBeanGridBlock(db);

        // count=1 nhưng đếm phải trả 3 (số KHỚP, không phải số hiển thị).
        var matched = await counter.CountMatchesAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlug\":\"hat-ca-phe\",\"count\":1}"));

        Assert.Equal(3, matched);
    }

    [Fact]
    public async Task CountMatches_LegacySingularCategorySlug_StillFilters()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");
        var other = SeedCategory(db, "do-uong", "Đồ uống");
        db.Products.AddRange(NewProduct("A", beans.Id), NewProduct("D", other.Id));
        await db.SaveChangesAsync();

        var counter = (IBlockMatchCounter)new ProductGridBlock(db);

        // Key số ít cũ: mọi trang đang chạy đều dùng dạng này.
        var legacy = await counter.CountMatchesAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlug\":\"hat-ca-phe\"}"));
        // Key số nhiều mới phải cho cùng kết quả.
        var modern = await counter.CountMatchesAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlugs\":[\"hat-ca-phe\"]}"));

        Assert.Equal(1, legacy);
        Assert.Equal(legacy, modern);
    }

    [Fact]
    public async Task CountMatches_EmptyCategory_CountsAllPublished()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");

        var published = NewProduct("A", beans.Id);
        var draft = NewProduct("B", beans.Id);
        draft.Status = ProductStatus.Draft;
        draft.PublishedAt = null;
        db.Products.AddRange(published, draft);
        await db.SaveChangesAsync();

        var counter = (IBlockMatchCounter)new ProductGridBlock(db);

        // Không lọc chuyên mục = tất cả, nhưng bản nháp không được tính.
        var matched = await counter.CountMatchesAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{}"));

        Assert.Equal(1, matched);
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"count-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static ProductCategory SeedCategory(AppDbContext db, string slug, string name)
    {
        var category = new ProductCategory { SiteId = Guid.Empty, Name = name, Slug = slug };
        db.ProductCategories.Add(category);
        return category;
    }

    private static Product NewProduct(string name, Guid categoryId) => new()
    {
        SiteId = Guid.Empty,
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-'),
        Sku = $"P-{Guid.NewGuid():N}"[..12],
        ShortDescription = "x",
        Description = "x",
        Status = ProductStatus.Published,
        PublishedAt = DateTime.UtcNow,
        SortOrder = 0,
        ProductCategoryId = categoryId
    };
}
