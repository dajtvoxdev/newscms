using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Tests;

/// <summary>
/// Font Google phải đi trọn vòng: chọn trong dropdown builder → giá trị font-family lưu vào CSS →
/// PageRenderer tự nhận ra và nhét thẻ link vào trang public. Mắt xích dễ đứt nhất là dropdown và
/// bộ dò dùng hai cách viết font-family khác nhau — lúc đó builder xem thì đẹp, trang public rơi
/// về font dự phòng và mất dấu tiếng Việt mà không có lỗi nào báo.
/// </summary>
public sealed class GoogleFontTests
{
    [Fact]
    public void ToCssValue_IsDetectedBack_ForEveryCatalogFont()
    {
        // Chính là hợp đồng giữa dropdown (ncFontFamilyOptions dựng '"Tên", nhóm') và DetectUsed.
        foreach (var font in GoogleFontCatalog.All)
        {
            var css = ".x{font-family:" + GoogleFontCatalog.ToCssValue(font) + "}";
            var used = GoogleFontCatalog.DetectUsed(css);

            Assert.Equal(new[] { font.Name }, used.Select(f => f.Name));
        }
    }

    [Fact]
    public void DetectUsed_FindsFontInInlineStyleAttribute()
    {
        var html = "<h1 style=\"font-family: 'Playfair Display', serif; color:red\">Xin chào</h1>";

        Assert.Equal(new[] { "Playfair Display" }, GoogleFontCatalog.DetectUsed(html).Select(f => f.Name));
    }

    [Fact]
    public void DetectUsed_IgnoresFontsOutsideCatalog()
    {
        var css = ".a{font-family:Arial, Helvetica, sans-serif}.b{font-family:\"Comic Sans MS\"}";

        Assert.Empty(GoogleFontCatalog.DetectUsed(css));
    }

    [Fact]
    public void DetectUsed_FindsFontInTokenCustomProperty()
    {
        // Token CSS khai báo font qua custom property --font-*; trang chỉ tham chiếu var(--font-*)
        // nên tên font literal chỉ xuất hiện ở một chỗ này — detector phải bắt được.
        var tokenCss = ":root{--font-display:\"Be Vietnam Pro\", sans-serif;--font-body:\"Work Sans\", system-ui}";

        var used = GoogleFontCatalog.DetectUsed(tokenCss).Select(f => f.Name).ToList();

        Assert.Equal(new[] { "Be Vietnam Pro", "Work Sans" }, used);
    }

    [Fact]
    public async Task RenderAsync_EmitsFontLink_WhenOnlyTokenCssNamesTheFont()
    {
        // Trường hợp thật của site dựng qua MCP: layout/page dùng var(--font-display), token CSS
        // là nơi duy nhất chứa tên font. Trước khi detector nhận custom property, trang như vậy
        // rơi về font hệ thống và mất dấu tiếng Việt mà không có lỗi nào.
        await using var db = NewDb();
        var id = await SeedPageAsync(db, customCss: ".hero{font-family:var(--font-display)}");

        var html = (await NewRenderer(db).RenderAsync(id, "vi"))!.Html;

        Assert.Contains("https://fonts.googleapis.com/css2?family=Be+Vietnam+Pro", html);
    }

    [Fact]
    public void DetectUsed_ReturnsCatalogOrder_NotDeclarationOrder()
    {
        // URL ổn định thì cache CDN/trình duyệt còn dùng lại được sau khi người dùng sửa CSS.
        var css = ".a{font-family:Oswald}.b{font-family:Inter}";

        Assert.Equal(new[] { "Inter", "Oswald" }, GoogleFontCatalog.DetectUsed(css).Select(f => f.Name));
    }

    [Fact]
    public void BuildStylesheetUrl_EncodesSpaces_AndAsksOnlyExistingWeights()
    {
        var font = GoogleFontCatalog.All.Single(f => f.Name == "Be Vietnam Pro");

        var url = GoogleFontCatalog.BuildStylesheetUrl(new[] { font });

        Assert.Equal(
            "https://fonts.googleapis.com/css2?family=Be+Vietnam+Pro:wght@" + font.Weights + "&display=swap",
            url);
    }

    [Fact]
    public void BuildStylesheetUrl_ReturnsNull_WhenNoFontUsed()
    {
        Assert.Null(GoogleFontCatalog.BuildStylesheetUrl(Array.Empty<GoogleFont>()));
    }

    [Fact]
    public async Task RenderAsync_EmitsFontLink_WhenPageCssUsesCatalogFont()
    {
        await using var db = NewDb();
        var id = await SeedPageAsync(db, customCss: ".hero{font-family:\"Be Vietnam Pro\", sans-serif}");

        var html = (await NewRenderer(db).RenderAsync(id, "vi"))!.Html;

        Assert.Contains("https://fonts.googleapis.com/css2?family=Be+Vietnam+Pro", html);
        // File font nằm ở gstatic — thiếu preconnect này là mất một vòng DNS+TLS trước khi có chữ.
        Assert.Contains("https://fonts.gstatic.com", html);
        // Phải là <link>: @import chỉ hợp lệ ở ĐẦU stylesheet, mà CSS 3 tầng được nối vào một
        // khối <style> duy nhất nên @import của trang sẽ nằm giữa và bị bỏ qua.
        Assert.DoesNotContain("@import url(https://fonts", html);
    }

    [Fact]
    public async Task RenderAsync_EmitsNoFontLink_WhenNoCatalogFontUsed()
    {
        await using var db = NewDb();
        var id = await SeedPageAsync(db, customCss: ".hero{font-family:Arial, sans-serif}");

        var html = (await NewRenderer(db).RenderAsync(id, "vi"))!.Html;

        Assert.DoesNotContain("fonts.googleapis.com", html);
    }

    private static async Task<Guid> SeedPageAsync(AppDbContext db, string customCss)
    {
        var page = new Page
        {
            Id = Guid.NewGuid(),
            Title = "Trang thử font",
            Slug = "font",
            Content = string.Empty,
            CompiledHtml = "<section class=\"hero\">Xin chào</section>",
            CustomCss = customCss,
            Status = BuilderPageStatus.Published,
            IsPublished = true
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    private static PageRenderer NewRenderer(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var signal = new SiteCacheSignal();
        var site = new TestCurrentSite();
        var accessor = new HttpContextAccessor();

        // Một registry DUY NHẤT cho cả RouteRegistry/TemplateResolver/PageRenderer — trước đây mỗi
        // lớp tự dựng một cái ở nhánh fallback nên cache ContentDetail per-request không dùng chung.
        var contentTypes = new NewsCMS.Infrastructure.Builder.ContentTypes.ContentTypeRegistry(
        [
            new NewsCMS.Infrastructure.Builder.ContentTypes.PostContentType(db),
            new NewsCMS.Infrastructure.Builder.ContentTypes.ProductContentType(db),
            new NewsCMS.Infrastructure.Builder.ContentTypes.CategoryContentType(db)
        ]);
        var routes = new RouteRegistry(db, cache, signal, site, contentTypes);

        return new PageRenderer(
            db,
            new FakeTokenCss(),
            new FakeSiteCode(),
            new DynamicBlockRenderer(new EmptyBlockRegistry(), cache, signal),
            site,
            accessor,
            new TemplateResolver(db, routes, cache, signal, site, contentTypes),
            contentTypes,
            new NewsCMS.Infrastructure.Builder.SeoMetaService(db),
            new NewsCMS.Infrastructure.Site.SiteUrlResolver(db, site, cache, signal, accessor));
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"gfont-{Guid.NewGuid()}")
        .Options);

    private sealed class FakeTokenCss : IDesignTokenCssBuilder
    {
        // Nội dung token CSS thật: :root khai báo font qua custom property — đúng hình dạng BuildCssAsync sinh ra.
        public Task<string> BuildCssAsync(CancellationToken ct = default) => Task.FromResult(
            ":root {\n  --font-display: \"Be Vietnam Pro\", sans-serif;\n  --font-body: \"Be Vietnam Pro\", sans-serif;\n}");
        public Task<string> GetCssHashAsync(CancellationToken ct = default) => Task.FromResult("hash");
    }

    private sealed class FakeSiteCode : ISiteCustomCodeService
    {
        public Task<SiteCustomCodeDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new SiteCustomCodeDto(null, null, null, null));

        public Task<NewsCMS.Application.Common.Result<SiteCustomCodeDto>> SaveAsync(
            SiteCustomCodeSaveRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyBlockRegistry : IDynamicBlockRegistry
    {
        public IDynamicBlock? Get(string key) => null;
        public IReadOnlyList<IDynamicBlock> All { get; } = Array.Empty<IDynamicBlock>();
    }

    private sealed class TestCurrentSite : NewsCMS.Application.Site.ICurrentSite
    {
        public Guid SiteId => Guid.Empty;
        public string Slug => "test";
        public string Theme => "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme) { }
    }
}
