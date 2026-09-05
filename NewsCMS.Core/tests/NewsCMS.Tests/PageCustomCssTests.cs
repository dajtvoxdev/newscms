using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// CustomCss của page phải đi trọn vòng đời: save → DTO → renderer nối SAU CompiledCss
 /// (nên selector trùng luôn thắng), và revision snapshot/restore không mất dữ liệu.
/// </summary>
public sealed class PageCustomCssTests
{
    [Fact]
    public async Task UpdateAsync_StoresCustomCss_AndDtoCarriesIt()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var created = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Trang thử", Slug: "thu", BuilderJson: null,
            CompiledHtml: "<section>hi</section>", CompiledCss: ".a{color:red}",
            CustomCss: ".ncv-dark{background:#000}", CustomJs: null, Kind: "Static"));
        Assert.True(created.Succeeded, created.Error);

        // Update đổi customCss — bản ghi cùng id.
        var updated = await service.UpdateAsync(created.Value!.Id, new BuilderPageSaveRequest(
            Title: "Trang thử", Slug: "thu", BuilderJson: null,
            CompiledHtml: "<section>hi</section>", CompiledCss: ".a{color:red}",
            CustomCss: ".ncv-brand{background:green}", CustomJs: null, Kind: "Static"));
        Assert.True(updated.Succeeded, updated.Error);
        Assert.Equal(".ncv-brand{background:green}", updated.Value!.CustomCss);
    }

    [Fact]
    public async Task Publish_SnapshotsCustomCss_RestoreBringsItBack()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var created = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Rev", Slug: "rev", BuilderJson: null,
            CompiledHtml: "<p>x</p>", CompiledCss: null,
            CustomCss: ".v1{}", CustomJs: "// v1", Kind: "Static"));
        Assert.True(created.Succeeded, created.Error);

        // Publish lần 1 (snapshot chứa .v1), rồi update sang .v2.
        var pub1 = await service.PublishAsync(created.Value!.Id, new BuilderPagePublishRequest("v1"));
        Assert.True(pub1.Succeeded, pub1.Error);

        await service.UpdateAsync(created.Value!.Id, new BuilderPageSaveRequest(
            Title: "Rev", Slug: "rev", BuilderJson: null,
            CompiledHtml: "<p>x</p>", CompiledCss: null,
            CustomCss: ".v2{}", CustomJs: null, Kind: "Static"));

        // Khôi phục v1 → customCss trở lại .v1.
        var restored = await service.RestoreRevisionAsync(created.Value!.Id, 1);
        Assert.True(restored.Succeeded, restored.Error);
        Assert.Equal(".v1{}", restored.Value!.CustomCss);
    }

    private static BuilderPageService NewService(AppDbContext db)
    {
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var site = new TestCurrentSite();
        var contentTypes = new NewsCMS.Infrastructure.Builder.ContentTypes.ContentTypeRegistry(
        [
            new NewsCMS.Infrastructure.Builder.ContentTypes.PostContentType(db),
            new NewsCMS.Infrastructure.Builder.ContentTypes.ProductContentType(db),
            new NewsCMS.Infrastructure.Builder.ContentTypes.CategoryContentType(db)
        ]);
        var registry = new NewsCMS.Infrastructure.Builder.RouteRegistry(
            db, cache, new NewsCMS.Infrastructure.Site.SiteCacheSignal(),
            site, contentTypes);
        return new BuilderPageService(db, new ContentSanitizer(), registry);
    }

    /// <summary>ICurrentSite tối giản cho test — không resolve qua middleware.</summary>
    private sealed class TestCurrentSite : NewsCMS.Application.Site.ICurrentSite
    {
        public Guid SiteId => Guid.Empty;
        public string Slug => "test";
        public string Theme => "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme) { }
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pagecss-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }
}
