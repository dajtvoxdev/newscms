using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Ai;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// Dựng ngữ cảnh site cho AI. Điểm quan trọng nhất là cách ly theo site: CMS này
/// chạy nhiều site trên cùng một DB, lọt nội dung của site khác vào prompt là lỗi
/// nghiêm trọng nhưng im lặng — AI vẫn trả lời trơn tru, chỉ là sai thương hiệu.
/// </summary>
public class SiteContextBuilderTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeCurrentSite(Guid siteId) : ICurrentSite
    {
        public Guid SiteId { get; private set; } = siteId;
        public string Slug { get; private set; } = "test";
        public string Theme { get; private set; } = "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme)
        {
            SiteId = siteId; Slug = slug; Theme = theme;
        }
    }

    /// <summary>
    /// DbContext + site phải là CÙNG một instance: query filter của AppDbContext
    /// đọc ICurrentSite để lọc, còn builder đọc lại để lấy tên site dự phòng.
    /// </summary>
    private static (AppDbContext Db, ICurrentSite Site) NewDb(Guid siteId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var site = new FakeCurrentSite(siteId);
        return (new AppDbContext(options, site), site);
    }

    private static Task<SiteContextSnapshot> BuildAsync(AppDbContext db, ICurrentSite site)
        => new SiteContextBuilder(db, site).BuildAsync();

    private static void Seed(AppDbContext db, Guid siteId, string name)
    {
        db.SiteSettings.Add(new SiteSetting { SiteId = siteId, Key = "Site.Name", Value = name });
        db.SiteSettings.Add(new SiteSetting { SiteId = siteId, Key = "Site.Description", Value = "Mô tả " + name });
        db.Categories.Add(new Category { SiteId = siteId, Name = "Chuyên mục " + name, Slug = "cm-" + siteId, IsActive = true });
        db.Products.Add(new Product
        {
            SiteId = siteId, Name = "Sản phẩm " + name, Slug = "sp", Sku = "SKU1",
            Description = "<p>x</p>", ShortDescription = "Mô tả ngắn", Price = 1000,
            Status = ProductStatus.Published
        });
        db.Posts.Add(new Post
        {
            SiteId = siteId, Title = "Bài " + name, Slug = "bai", Content = "<p>Nội dung</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task BuildAsync_ChiLayDuLieuCuaSiteHienTai()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;
        Seed(db, SiteA, "SITE_A");
        Seed(db, SiteB, "SITE_B");

        var snapshot = await BuildAsync(db, site);

        Assert.Equal("SITE_A", snapshot.SiteName);
        Assert.Contains("Chuyên mục SITE_A", snapshot.Categories);
        Assert.Contains("Sản phẩm SITE_A", snapshot.Products[0]);
        Assert.Equal("Bài SITE_A", snapshot.RecentPosts[0].Title);

        // Không một mảnh nào của site B được lọt vào.
        var text = SiteContextFormatter.Format(snapshot);
        Assert.DoesNotContain("SITE_B", text);
    }

    [Fact]
    public async Task BuildAsync_ChiLayBaiDaXuatBan()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;
        db.Posts.Add(new Post
        {
            SiteId = SiteA, Title = "Bài nháp", Slug = "nhap", Content = "<p>x</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(), Status = PostStatus.Draft
        });
        db.Posts.Add(new Post
        {
            SiteId = SiteA, Title = "Bài đã đăng", Slug = "dang", Content = "<p>x</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var snapshot = await BuildAsync(db, site);

        // Bài nháp có thể là thử nghiệm hoặc sai sót — bắt AI chước giọng là phản tác dụng.
        Assert.Single(snapshot.RecentPosts);
        Assert.Equal("Bài đã đăng", snapshot.RecentPosts[0].Title);
    }

    [Fact]
    public async Task BuildAsync_BaiMoiNhatDungTruoc()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;
        db.Posts.Add(new Post
        {
            SiteId = SiteA, Title = "Bài cũ", Slug = "cu", Content = "<p>x</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-10)
        });
        db.Posts.Add(new Post
        {
            SiteId = SiteA, Title = "Bài mới", Slug = "moi", Content = "<p>x</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var snapshot = await BuildAsync(db, site);

        Assert.Equal("Bài mới", snapshot.RecentPosts[0].Title);
    }

    [Fact]
    public async Task BuildAsync_BoTheHtmlKhoiTrichDoanThanBai()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;
        db.Posts.Add(new Post
        {
            SiteId = SiteA, Title = "Bài có HTML", Slug = "html",
            Content = "<h2>Tiêu đề phụ</h2><p>Đoạn <strong>đậm</strong> &amp; ký tự đặc biệt</p>",
            CategoryId = Guid.NewGuid(), AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var snapshot = await BuildAsync(db, site);
        var excerpt = snapshot.RecentPosts[0].BodyExcerpt;

        Assert.DoesNotContain("<", excerpt);
        Assert.DoesNotContain(">", excerpt);
        // Entity HTML phải được giải mã để AI đọc ra chữ thật.
        Assert.Contains("& ký tự đặc biệt", excerpt);
    }

    [Fact]
    public async Task BuildAsync_SiteChuaCoGi_TraSnapshotRong()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;

        var snapshot = await BuildAsync(db, site);

        Assert.True(snapshot.IsEmpty);
        Assert.Equal(string.Empty, SiteContextFormatter.Format(snapshot));
    }

    [Fact]
    public async Task BuildAsync_ChiLaySanPhamDangBan()
    {
        var (db, site) = NewDb(SiteA);
        await using var _guard = db;
        db.Products.Add(new Product
        {
            SiteId = SiteA, Name = "SP nháp", Slug = "a", Sku = "S1",
            Description = "<p>x</p>", Price = 1, Status = ProductStatus.Draft
        });
        db.Products.Add(new Product
        {
            SiteId = SiteA, Name = "SP đang bán", Slug = "b", Sku = "S2",
            Description = "<p>x</p>", Price = 1, Status = ProductStatus.Published
        });
        await db.SaveChangesAsync();

        var snapshot = await BuildAsync(db, site);

        Assert.Single(snapshot.Products);
        Assert.Contains("SP đang bán", snapshot.Products[0]);
    }
}
