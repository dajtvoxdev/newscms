using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

public sealed class CoffeeBeanGridBlockTests
{
    /// <summary>Bảng hướng dẫn pha nhập ở admin đi qua ContentSanitizer.Sanitize (whitelist hẹp).</summary>
    private const string BrewingGuideHtml =
        "<p style=\"margin:0 0 10px;font-weight:600;color:rgba(111,88,55,1)\">Hướng dẫn pha:</p>" +
        "<table style=\"width:100%;border-collapse:collapse\"><tbody>" +
        "<tr><td style=\"width:80px;padding:8px 0;border-bottom:1px solid rgba(93,46,13,.15);font-style:italic\">Pha máy:</td>" +
        "<td style=\"padding:8px 0;border-bottom:1px solid rgba(93,46,13,.15)\">1:2.5, 90–95°C, 25s+2s</td></tr>" +
        "</tbody></table>";

    [Fact]
    public void ContentSanitizer_KeepsBrewingGuideTableStyles()
    {
        var sanitized = new ContentSanitizer().Sanitize(BrewingGuideHtml);

        Assert.Contains("<table", sanitized);
        Assert.Contains("border-collapse", sanitized);
        Assert.Contains("border-bottom", sanitized);
        Assert.Contains("font-style", sanitized);
        Assert.Contains("Pha máy:", sanitized);
    }

    [Fact]
    public async Task RenderAsync_OrdersBySortOrder_AndFiltersByCategory()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");
        var other = SeedCategory(db, "do-uong", "Đồ uống");

        db.Products.AddRange(
            NewProduct("Ethiopia", "Ethiopia", beans.Id, sortOrder: 20),
            NewProduct("Lotus Arabica", "Việt Nam", beans.Id, sortOrder: 10),
            NewProduct("Bạc xỉu", "Việt Nam", other.Id, sortOrder: 5));
        await db.SaveChangesAsync();

        var html = await new CoffeeBeanGridBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlug\":\"hat-ca-phe\",\"count\":6}"));

        Assert.DoesNotContain("Bạc xỉu", html);
        Assert.True(html.IndexOf("Lotus Arabica", StringComparison.Ordinal)
                    < html.IndexOf("Ethiopia", StringComparison.Ordinal),
            "SortOrder nhỏ hơn phải hiển thị trước.");
        Assert.Contains("chu-card", html);
        Assert.Contains("chu-tag", html);
    }

    [Fact]
    public async Task RenderAsync_SkipsDraftAndRespectsCount()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");

        var draft = NewProduct("Liberica", "Việt Nam", beans.Id, sortOrder: 30);
        draft.Status = ProductStatus.Draft;
        draft.PublishedAt = null;

        db.Products.AddRange(
            NewProduct("Ethiopia", "Ethiopia", beans.Id, sortOrder: 20),
            NewProduct("Lotus Arabica", "Việt Nam", beans.Id, sortOrder: 10),
            draft);
        await db.SaveChangesAsync();

        var html = await new CoffeeBeanGridBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlug\":\"hat-ca-phe\",\"count\":1}"));

        Assert.DoesNotContain("Liberica", html);
        Assert.DoesNotContain("Ethiopia", html);
        Assert.Contains("Lotus Arabica", html);
    }

    [Fact]
    public async Task RenderAsync_NoBeans_ReturnsCommentInsteadOfBrokenMarkup()
    {
        await using var db = NewDb();

        var html = await new CoffeeBeanGridBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"categorySlug\":\"hat-ca-phe\"}"));

        Assert.Equal("<!-- coffee-bean-grid: no beans -->", html);
    }

    [Fact]
    public async Task RenderAsync_FeaturedOnly_KeepsOnlyFeaturedBeans()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");

        var featured = NewProduct("Ethiopia", "Ethiopia", beans.Id, sortOrder: 20);
        featured.IsFeatured = true;

        db.Products.AddRange(featured, NewProduct("Lotus Arabica", "Việt Nam", beans.Id, sortOrder: 10));
        await db.SaveChangesAsync();

        var html = await new CoffeeBeanGridBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi",
                "{\"categorySlug\":\"hat-ca-phe\",\"featuredOnly\":true}"));

        Assert.Contains("Ethiopia", html);
        Assert.DoesNotContain("Lotus Arabica", html);
    }

    /// <summary>
    /// Props đi qua sanitizer/serializer bị escape thành {&amp;quot;k&amp;quot;:...}; renderer phải
    /// de-entitize trước khi parse, nếu không block rơi về props mặc định (lấy nhầm mọi chuyên mục).
    /// </summary>
    [Fact]
    public async Task Renderer_PassesPropsThroughSanitizedHtml()
    {
        await using var db = NewDb();
        var beans = SeedCategory(db, "hat-ca-phe", "Hạt cà phê");
        var other = SeedCategory(db, "do-uong", "Đồ uống");
        db.Products.AddRange(
            NewProduct("Lotus Arabica", "Việt Nam", beans.Id, sortOrder: 10),
            NewProduct("Bạc xỉu", "Việt Nam", other.Id, sortOrder: 5));
        await db.SaveChangesAsync();

        var pageHtml = new ContentSanitizer().SanitizeBuilder(
            "<div data-nc-block=\"coffee-bean-grid\" data-nc-props='{\"categorySlug\":\"hat-ca-phe\"}'></div>");

        var renderer = new NewsCMS.Infrastructure.Builder.DynamicBlockRenderer(
            new NewsCMS.Infrastructure.Builder.DynamicBlockRegistry(new[] { new CoffeeBeanGridBlock(db) }),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            new NewsCMS.Infrastructure.Site.SiteCacheSignal());

        var html = await renderer.RenderAsync(pageHtml, Guid.Empty, "vi");

        Assert.Contains("Lotus Arabica", html);
        Assert.DoesNotContain("Bạc xỉu", html);
    }

    private static AppDbContext NewDb()
    {
        // Không gọi ConfigureWarnings: mỗi lần gọi buộc EF dựng internal service provider mới,
        // quá 20 provider là cả test suite ném ManyServiceProvidersCreatedWarning.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"beans-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static ProductCategory SeedCategory(AppDbContext db, string slug, string name)
    {
        var category = new ProductCategory { SiteId = Guid.Empty, Name = name, Slug = slug };
        db.ProductCategories.Add(category);
        return category;
    }

    private static Product NewProduct(string name, string origin, Guid categoryId, int sortOrder) => new()
    {
        SiteId = Guid.Empty,
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-'),
        Sku = $"BEAN-{Guid.NewGuid():N}"[..12],
        ShortDescription = origin,
        Description = BrewingGuideHtml,
        Status = ProductStatus.Published,
        PublishedAt = DateTime.UtcNow,
        SortOrder = sortOrder,
        ProductCategoryId = categoryId
    };
}
