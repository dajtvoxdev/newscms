using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Tests;

/// <summary>
/// Shell (SiteLayout) đi qua đúng pipeline như nội dung trang:
///   • khối động trong shell (site-menu ở nav header/footer) được render, không ra placeholder rỗng;
///   • CustomCss của layout nối SAU CompiledCss nên selector trùng thì CSS viết tay thắng.
/// Hai điều này là điều kiện để "sửa nav/footer trong builder" thực sự hiện ra trang public.
/// </summary>
public sealed class ShellRenderingTests
{
    [Fact]
    public async Task RenderAsync_ShellDynamicBlock_IsRendered()
    {
        await using var db = NewDb();
        SeedMenu(db);
        SeedShell(db,
            "<header><div data-nc-block=\"site-menu\" data-nc-props='{\"location\":\"header\"}'></div></header>"
            + "<main data-nc-body></main><footer>foot</footer>");
        var pageId = SeedPage(db, "<section>Nội dung trang</section>");
        await db.SaveChangesAsync();

        var html = (await NewRenderer(db).RenderAsync(pageId, "vi"))!.Html;

        Assert.Contains("nc-menu-link", html);
        Assert.Contains("/lien-he", html);
        Assert.Contains("Nội dung trang", html);
        // Placeholder phải được đổ ruột, không còn là thẻ rỗng.
        Assert.DoesNotContain("data-nc-block=\"site-menu\"></div>", html);
    }

    [Fact]
    public async Task RenderAsync_LayoutCustomCss_ComesAfterCompiledCss()
    {
        await using var db = NewDb();
        SeedShell(db, "<header>h</header><main data-nc-body></main>",
            compiledCss: ".nc-menu{color:red}", customCss: ".nc-menu{color:green}");
        var pageId = SeedPage(db, "<p>x</p>");
        await db.SaveChangesAsync();

        var html = (await NewRenderer(db).RenderAsync(pageId, "vi"))!.Html;

        var compiled = html.IndexOf(".nc-menu{color:red}", StringComparison.Ordinal);
        var custom = html.IndexOf(".nc-menu{color:green}", StringComparison.Ordinal);
        Assert.True(compiled >= 0, "CompiledCss của layout phải có trong trang.");
        Assert.True(custom >= 0, "CustomCss của layout phải có trong trang.");
        Assert.True(compiled < custom, "CustomCss phải nối SAU CompiledCss để selector trùng thì nó thắng.");
    }

    /// <summary>
    /// Khối không đăng ký (theme cũ, key gõ sai) chỉ được thêm comment — shell vẫn ra header/footer.
    /// Nếu bước này ném lỗi thì một key sai làm sập toàn site, không riêng một khối.
    /// </summary>
    [Fact]
    public async Task RenderAsync_UnknownShellBlock_DoesNotBreakPage()
    {
        await using var db = NewDb();
        SeedShell(db, "<header><div data-nc-block=\"khong-ton-tai\"></div></header><main data-nc-body></main>");
        var pageId = SeedPage(db, "<p>nội dung</p>");
        await db.SaveChangesAsync();

        var html = (await NewRenderer(db).RenderAsync(pageId, "vi"))!.Html;

        Assert.Contains("<header", html);
        Assert.Contains("nội dung", html);
        Assert.Contains("unknown dynamic block", html);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static PageRenderer NewRenderer(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var signal = new SiteCacheSignal();
        var site = new TestCurrentSite();
        var registry = new DynamicBlockRegistry([new NewsCMS.Infrastructure.Builder.Blocks.SiteMenuBlock(db)]);
        var accessor = new HttpContextAccessor();

        // Một registry DUY NHẤT dùng chung, thay cho nhánh fallback mỗi lớp tự dựng một cái.
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
            new DynamicBlockRenderer(registry, cache, signal),
            site,
            accessor,
            // Resolver thật, không fake: các test này không có template nào nên nó luôn trả null,
            // đúng đường mà site chưa nâng cấp sẽ đi.
            new TemplateResolver(db, routes, cache, signal, site, contentTypes),
            contentTypes,
            new NewsCMS.Infrastructure.Builder.SeoMetaService(db),
            new NewsCMS.Infrastructure.Site.SiteUrlResolver(db, site, cache, signal, accessor));
    }

    private static void SeedMenu(AppDbContext db)
    {
        var menu = new Menu { SiteId = Guid.Empty, Name = "Menu chính", Location = "header" };
        menu.Items.Add(new MenuItem { MenuId = menu.Id, Title = "Liên hệ", Url = "/lien-he", Order = 0 });
        db.Menus.Add(menu);
    }

    private static void SeedShell(AppDbContext db, string html, string? compiledCss = null, string? customCss = null)
        => db.SiteLayouts.Add(new SiteLayout
        {
            SiteId = Guid.Empty,
            Key = "shell-default",
            Name = "Shell mặc định",
            Kind = LayoutKind.Shell,
            IsDefault = true,
            CompiledHtml = html,
            CompiledCss = compiledCss,
            CustomCss = customCss
        });

    private static Guid SeedPage(AppDbContext db, string compiledHtml)
    {
        var page = new Page
        {
            SiteId = Guid.Empty,
            Title = "Trang thử",
            Slug = "thu",
            Content = string.Empty,   // cột bắt buộc (trang builder không dùng, nội dung ở CompiledHtml)
            CompiledHtml = compiledHtml,
            Status = BuilderPageStatus.Published,
            IsPublished = true
        };
        db.Pages.Add(page);
        return page.Id;
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"shellrender-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeTokenCss : IDesignTokenCssBuilder
    {
        public Task<string> BuildCssAsync(CancellationToken ct = default) => Task.FromResult(":root{}");
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

    private sealed class TestCurrentSite : NewsCMS.Application.Site.ICurrentSite
    {
        public Guid SiteId => Guid.Empty;
        public string Slug => "test";
        public string Theme => "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme) { }
    }
}
