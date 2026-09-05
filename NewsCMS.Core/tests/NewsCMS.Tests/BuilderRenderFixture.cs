using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ đồ nghề dùng chung cho các test trang chi tiết (template theo URL).
///
/// Cố tình nối <see cref="TemplateResolver"/> và <see cref="RouteRegistry"/> THẬT thay vì fake:
/// chính chuỗi "URL → trang cha → template" mới là thứ đang được kiểm chứng, fake nó đi thì test
/// chỉ còn xác nhận chính cái fake.
/// </summary>
internal sealed class BuilderRenderFixture : IDisposable
{
    public const string Culture = "vi";

    public AppDbContext Db { get; }
    public MemoryCache Cache { get; }
    public SiteCacheSignal Signal { get; }
    public Infrastructure.Builder.ContentTypes.ContentTypeRegistry ContentTypes { get; }
    public RouteRegistry Routes { get; }
    public TemplateResolver Templates { get; }
    public Infrastructure.Builder.SeoMetaService SeoMetas { get; }
    /// <summary>
    /// Base URL cho canonical/og:url/og:image và &lt;loc&gt; của sitemap. Fixture không có
    /// HttpContext nên nó chỉ đọc domain từ DB — mặc định site test bỏ trống PrimaryDomain,
    /// dùng <see cref="SetPrimaryDomain"/> khi test cần URL tuyệt đối.
    /// </summary>
    public Infrastructure.Site.SiteUrlResolver SiteUrls { get; }
    public Application.Site.ICurrentSite Site => _site;

    private readonly TestCurrentSite _site = new();

    public BuilderRenderFixture(string prefix)
    {
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{prefix}-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, _site);

        Cache = new MemoryCache(new MemoryCacheOptions());
        Signal = new SiteCacheSignal();
        ContentTypes = new Infrastructure.Builder.ContentTypes.ContentTypeRegistry(
        [
            new Infrastructure.Builder.ContentTypes.PostContentType(Db),
            new Infrastructure.Builder.ContentTypes.ProductContentType(Db),
            new Infrastructure.Builder.ContentTypes.CategoryContentType(Db)
        ]);
        Routes = new RouteRegistry(Db, Cache, Signal, _site, ContentTypes);
        Templates = new TemplateResolver(Db, Routes, Cache, Signal, _site, ContentTypes);
        SeoMetas = new Infrastructure.Builder.SeoMetaService(Db);
        SiteUrls = new Infrastructure.Site.SiteUrlResolver(Db, _site, Cache, Signal, new HttpContextAccessor());

        Db.Sites.Add(new Domain.Entities.Site.Site
        {
            Id = _site.SiteId,
            Name = "Test Site",
            Slug = _site.Slug,
            DefaultCulture = "vi",
            SupportedCultures = "vi,en",
            DefaultTheme = "Universal"
        });
        Db.SaveChanges();
    }

    /// <summary>
    /// PageRenderer nối đủ dây. <paramref name="blocks"/> là các khối động sẽ có mặt trong
    /// registry — mặc định rỗng để test render không phụ thuộc khối nào.
    /// </summary>
    public PageRenderer NewPageRenderer(params IDynamicBlock[] blocks) => new(
        Db,
        new FakeTokenCss(),
        new FakeSiteCode(),
        NewBlockRenderer(blocks),
        _site,
        new HttpContextAccessor(),
        Templates,
        ContentTypes,
        SeoMetas,
        SiteUrls);

    public DynamicBlockRenderer NewBlockRenderer(params IDynamicBlock[] blocks) =>
        new(new DynamicBlockRegistry(blocks), Cache, Signal);

    public SiteBuilderApi NewSiteBuilderApi(params IDynamicBlock[] blocks)
    {
        var sanitizer = new ContentSanitizer();
        var blockReg = new DynamicBlockRegistry(blocks);
        var pageSvc = new BuilderPageService(Db, sanitizer, Routes);
        var layoutSvc = new SiteLayoutService(Db, sanitizer);
        var catSvc = new BuilderCategoryService(Db, Routes);
        var renderer = NewPageRenderer(blocks);

        return new SiteBuilderApi(
            Db,
            _site,
            sanitizer,
            Routes,
            pageSvc,
            layoutSvc,
            catSvc,
            new FakeSiteCode(),
            SeoMetas,
            renderer,
            blockReg);
    }

    // ── Seed ──────────────────────────────────────────────────────────────────────

    /// <summary>Trang builder đã publish. <paramref name="slug"/> rỗng = trang chủ.</summary>
    public Page AddPage(
        string title, string slug, string? compiledHtml = "<main>noi dung</main>",
        PageKind kind = PageKind.Landing, Guid? parentPageId = null,
        bool isDefaultTemplate = false, bool published = true)
    {
        var page = new Page
        {
            SiteId = Guid.Empty,
            Title = title,
            Slug = slug,
            Content = string.Empty,
            CompiledHtml = compiledHtml,
            Kind = kind,
            ParentPageId = parentPageId,
            IsDefaultTemplate = isDefaultTemplate,
            Status = published ? BuilderPageStatus.Published : BuilderPageStatus.Draft,
            IsPublished = published
        };

        Db.Pages.Add(page);
        Db.SaveChanges();
        return page;
    }

    public Category AddCategory(string name, string slug, Guid? templatePageId = null)
    {
        var cat = new Category
        {
            SiteId = Guid.Empty,
            Name = name,
            Slug = slug,
            PathSlug = slug,
            Type = CategoryType.Post,
            IsActive = true,
            TemplatePageId = templatePageId
        };

        Db.Categories.Add(cat);
        Db.SaveChanges();
        return cat;
    }

    public Post AddPost(string title, string slug, Guid categoryId, string content = "<p>than bai</p>")
    {
        var post = new Post
        {
            SiteId = Guid.Empty,
            Title = title,
            Slug = slug,
            Content = content,
            Excerpt = "tom tat",
            CategoryId = categoryId,
            AuthorId = Guid.NewGuid(),
            Status = PostStatus.Published,
            PublishedAt = DateTime.UtcNow
        };

        Db.Posts.Add(post);
        Db.SaveChanges();
        return post;
    }

    public ProductCategory AddProductCategory(string name, string slug)
    {
        var cat = new ProductCategory { SiteId = Guid.Empty, Name = name, Slug = slug, IsActive = true };
        Db.ProductCategories.Add(cat);
        Db.SaveChanges();
        return cat;
    }

    public Product AddProduct(string name, string slug, Guid categoryId)
    {
        var product = new Product
        {
            SiteId = Guid.Empty,
            Name = name,
            Slug = slug,
            Sku = slug.ToUpperInvariant(),
            Description = "<p>mo ta</p>",
            Price = 100_000m,
            ProductCategoryId = categoryId,
            Status = ProductStatus.Published,
            PublishedAt = DateTime.UtcNow
        };

        Db.Products.Add(product);
        Db.SaveChanges();
        return product;
    }

    /// <summary>SiteRoute chuẩn hoá. Đây là bảng mà RouteRegistry.ResolveAsync tra.</summary>
    public SiteRoute AddRoute(string path, RouteType type, Guid targetId)
    {
        var route = new SiteRoute
        {
            SiteId = Guid.Empty,
            Path = IRouteRegistry.NormalizePath(path),
            Culture = Culture,
            RouteType = type,
            TargetId = targetId,
            IsPrimary = true
        };

        Db.SiteRoutes.Add(route);
        Db.SaveChanges();
        Routes.Invalidate();
        return route;
    }

    /// <summary>
    /// Gán PrimaryDomain cho site — cần cho các thẻ SEO phải là URL tuyệt đối (canonical, og:url,
    /// og:image). Fixture mặc định để trống để giữ hành vi "render ngoài pipeline HTTP".
    /// </summary>
    public void SetPrimaryDomain(string domain)
    {
        var site = Db.Sites.First(s => s.Id == _site.SiteId);
        site.PrimaryDomain = domain;
        Db.SaveChanges();

        // SiteUrlResolver cache domain theo SiteCacheSignal — không bỏ cache thì test set domain
        // xong vẫn nhận giá trị cũ (hoặc rỗng, nếu resolver đã tra trước đó).
        Signal.Invalidate();
    }

    /// <summary>
    /// Thêm host vào bảng SiteDomains mà KHÔNG set Site.PrimaryDomain — đúng trạng thái của
    /// chu-kafe trên DB thật, và là lý do sitemap từng phát <c>https://localhost/...</c>.
    /// </summary>
    public Domain.Entities.Site.SiteDomain AddSiteDomain(string host, bool isPrimary = true)
    {
        var domain = new Domain.Entities.Site.SiteDomain
        {
            SiteId = _site.SiteId,
            Host = host,
            IsPrimary = isPrimary
        };

        Db.SiteDomains.Add(domain);
        Db.SaveChanges();
        Signal.Invalidate();
        return domain;
    }

    public void Dispose()
    {
        Cache.Dispose();
        Db.Dispose();
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────────

    private sealed class FakeTokenCss : IDesignTokenCssBuilder
    {
        public Task<string> BuildCssAsync(CancellationToken ct = default) => Task.FromResult(":root{}");
        public Task<string> GetCssHashAsync(CancellationToken ct = default) => Task.FromResult("hash");
    }

    private sealed class FakeSiteCode : ISiteCustomCodeService
    {
        public Task<SiteCustomCodeDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new SiteCustomCodeDto(null, null, null, null));

        public Task<Result<SiteCustomCodeDto>> SaveAsync(
            SiteCustomCodeSaveRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result<SiteCustomCodeDto>.Success(new SiteCustomCodeDto(request.CustomCss, request.CustomJs, request.HeadHtml, request.BodyEndHtml)));
    }

    private sealed class TestCurrentSite : NewsCMS.Application.Site.ICurrentSite
    {
        public Guid SiteId { get; set; } = Guid.NewGuid();
        public string Slug => "test";
        public string Theme => "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme) { }
    }
}
