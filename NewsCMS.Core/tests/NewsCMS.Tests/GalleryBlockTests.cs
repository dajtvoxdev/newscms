using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// Khối Thư viện ảnh (Phase 5). Ba điều bắt buộc theo plan:
///   • Bố cục masonry bằng CSS thuần (<c>column-count</c>), KHÔNG dùng Masonry.js.
///   • Mỗi ảnh có <c>loading="lazy"</c> + <c>width</c>/<c>height</c> để không gây layout shift.
///   • Chọn tay giữ đúng thứ tự người dùng kéo (item-picker), không theo thứ tự SQL trả về.
/// </summary>
public sealed class GalleryBlockTests
{
    [Fact]
    public async Task RenderAsync_Masonry_UsesColumnCount_NotJs()
    {
        await using var db = NewDb();
        SeedImages(db, 4);
        await db.SaveChangesAsync();

        var html = await NewBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"layout\":\"masonry\",\"columns\":3}"));

        Assert.Contains("column-count", html);
        Assert.DoesNotContain("Masonry", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_EmitsLazyLoadingAndDimensions()
    {
        await using var db = NewDb();
        SeedImages(db, 1);
        await db.SaveChangesAsync();

        var html = await NewBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{}"));

        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("width=\"800\"", html);
        Assert.Contains("height=\"600\"", html);
    }

    [Fact]
    public async Task RenderAsync_ManualSelection_KeepsUserOrder()
    {
        await using var db = NewDb();
        var imgs = SeedImages(db, 3);
        await db.SaveChangesAsync();

        // Kéo theo thứ tự ngược với thứ tự tạo.
        var order = new[] { imgs[2].Id, imgs[0].Id, imgs[1].Id };
        var propsJson = "{\"mediaIds\":[\"" + string.Join("\",\"", order) + "\"]}";

        var html = await NewBlock(db).RenderAsync(new DynamicBlockContext(Guid.Empty, "vi", propsJson));

        var i2 = html.IndexOf("/img2.jpg", StringComparison.Ordinal);
        var i0 = html.IndexOf("/img0.jpg", StringComparison.Ordinal);
        var i1 = html.IndexOf("/img1.jpg", StringComparison.Ordinal);
        Assert.True(i2 >= 0 && i0 >= 0 && i1 >= 0, "Cả 3 ảnh đã chọn phải xuất hiện.");
        Assert.True(i2 < i0 && i0 < i1, "Thứ tự phải theo mediaIds người dùng kéo, không theo SQL.");
    }

    [Fact]
    public async Task CountMatches_CountsAllImages_IgnoringCount()
    {
        await using var db = NewDb();
        SeedImages(db, 5);
        await db.SaveChangesAsync();

        var counter = (IBlockMatchCounter)NewBlock(db);
        var matched = await counter.CountMatchesAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"count\":2}"));

        Assert.Equal(5, matched);
    }

    private static GalleryBlock NewBlock(AppDbContext db) => new(db, new FakeEnv());

    private static List<Media> SeedImages(AppDbContext db, int n)
    {
        var list = new List<Media>();
        for (var i = 0; i < n; i++)
        {
            var m = new Media
            {
                SiteId = Guid.Empty,
                FileName = $"img{i}.jpg",
                StorageKey = $"key{i}",
                FilePath = $"/img{i}.jpg",
                MimeType = "image/jpeg",
                Kind = "image",
                Width = 800,
                Height = 600,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            };
            list.Add(m);
            db.Medias.Add(m);
        }
        return list;
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"gallery-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>WebRootPath rỗng → BlockAssets.Versioned trả path trần, đủ cho test render.</summary>
    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
    }
}
