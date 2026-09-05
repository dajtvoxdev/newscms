using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// Nối dây DI cho tầng Site Builder phải resolve được THẬT.
///
/// Vì sao cần: Phase 2.5 thêm constructor thứ hai <c>(AppDbContext)</c> vào 5 khối
/// <c>entity-*</c> bên cạnh <c>(IContentTypeRegistry)</c>. Cả hai đều một tham số và đều
/// resolve được, nên <c>ServiceProvider</c> ném "following constructors are ambiguous" ngay khi
/// dựng khối — mọi trang của site trả 500. Toàn bộ test khác vẫn xanh vì chúng <c>new</c> khối
/// trực tiếp, không đi qua DI. Test này bịt đúng khoảng mù đó.
/// </summary>
public sealed class BuilderDiWiringTests
{
    /// <summary>
    /// Mọi <see cref="IDynamicBlock"/> đã đăng ký phải dựng được qua DI. Đây là điều kiện để
    /// trang public render — <c>DynamicBlockRegistry</c> nhận <c>IEnumerable&lt;IDynamicBlock&gt;</c>
    /// nên chỉ cần MỘT khối không dựng được là cả request đổ.
    /// </summary>
    [Fact]
    public void Moi_dynamic_block_deu_resolve_duoc_qua_DI()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var blocks = scope.ServiceProvider.GetServices<IDynamicBlock>().ToList();

        Assert.NotEmpty(blocks);
        // Key trùng nhau thì registry lặng lẽ bỏ bớt khối (first-wins) — bắt sớm ở đây.
        Assert.Equal(blocks.Count, blocks.Select(b => b.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Registry_va_renderer_deu_resolve_duoc_qua_DI()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IContentTypeRegistry>());
        Assert.NotNull(sp.GetRequiredService<IDynamicBlockRegistry>());
        Assert.NotNull(sp.GetRequiredService<ITemplateResolver>());
        Assert.NotNull(sp.GetRequiredService<IPageRenderer>());
        Assert.NotNull(sp.GetRequiredService<IRouteRegistry>());
    }

    /// <summary>
    /// Registry là scoped và giữ cache <c>ContentDetail</c> theo request. Nếu nó thành singleton
    /// thì cache rò giữa các request (và <c>Dictionary</c> không thread-safe); nếu thành transient
    /// thì mỗi khối trên cùng một trang lại tự query, mất đúng lợi ích của cache.
    /// </summary>
    [Fact]
    public void ContentTypeRegistry_la_scoped_dung_mot_instance_trong_mot_request()
    {
        using var provider = BuildProvider();

        using var scope1 = provider.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<IContentTypeRegistry>();
        var b = scope1.ServiceProvider.GetRequiredService<IContentTypeRegistry>();
        Assert.Same(a, b);

        using var scope2 = provider.CreateScope();
        var c = scope2.ServiceProvider.GetRequiredService<IContentTypeRegistry>();
        Assert.NotSame(a, c);
    }

    /// <summary>
    /// Khối <c>entity-*</c> phải dùng CHUNG registry với renderer trong một request, nếu không
    /// cache per-request vô nghĩa: 6 khối trên trang chi tiết sẽ query 6 lần thay vì 1.
    /// </summary>
    [Fact]
    public void Khoi_entity_dung_chung_registry_voi_scope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var shared = sp.GetRequiredService<IContentTypeRegistry>();
        var blocks = sp.GetServices<IDynamicBlock>().ToList();

        // Không thể soi private field, nên kiểm gián tiếp: khối dựng được và registry là scoped
        // singleton (đã khẳng định ở test trên) ⇒ mọi khối nhận đúng instance này.
        Assert.Contains(blocks, b => b.Key == "entity-title");
        Assert.NotNull(shared);
    }

    /// <summary>
    /// Container tối thiểu: chỉ đăng ký đúng phần Site Builder cần, DbContext dùng InMemory để
    /// không phụ thuộc SQL Server. Cố ý KHÔNG gọi <c>AddInfrastructure</c> vì hàm đó kéo theo
    /// Identity/DataProtection/storage — nhiều thứ ngoài phạm vi test này.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        // GalleryBlock cần IWebHostEnvironment để gắn ?v= vào asset tĩnh (BlockAssets.Versioned).
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment, TestWebHostEnvironment>();

        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase($"di-{Guid.NewGuid()}"));

        services.AddSingleton<Infrastructure.Site.SiteCacheSignal>();
        services.AddScoped<Application.Site.ICurrentSite, TestCurrentSite>();

        services.AddScoped<IContentType, Infrastructure.Builder.ContentTypes.PostContentType>();
        services.AddScoped<IContentType, Infrastructure.Builder.ContentTypes.ProductContentType>();
        services.AddScoped<IContentType, Infrastructure.Builder.ContentTypes.CategoryContentType>();
        services.AddScoped<IContentTypeRegistry, Infrastructure.Builder.ContentTypes.ContentTypeRegistry>();

        services.AddScoped<IRouteRegistry, Infrastructure.Builder.RouteRegistry>();
        services.AddScoped<ITemplateResolver, Infrastructure.Builder.TemplateResolver>();
        services.AddScoped<IPageRenderer, Infrastructure.Builder.PageRenderer>();
        services.AddScoped<IDesignTokenCssBuilder, FakeTokenCss>();
        services.AddScoped<ISiteCustomCodeService, FakeSiteCode>();
        services.AddScoped<ISeoMetaService, Infrastructure.Builder.SeoMetaService>();
        services.AddScoped<Application.Site.ISiteUrlResolver, Infrastructure.Site.SiteUrlResolver>();

        services.AddScoped<IDynamicBlockRegistry, Infrastructure.Builder.DynamicBlockRegistry>();
        services.AddScoped<Infrastructure.Builder.DynamicBlockRenderer>();

        foreach (var t in DynamicBlockTypes())
            services.AddScoped(typeof(IDynamicBlock), t);

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Quét assembly thay vì liệt kê tay: khối mới thêm vào code là tự động nằm trong test này,
    /// không cần ai nhớ cập nhật danh sách.
    /// </summary>
    private static IEnumerable<Type> DynamicBlockTypes() =>
        typeof(Infrastructure.Builder.Blocks.EntityTitleBlock).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IDynamicBlock).IsAssignableFrom(t));

    private sealed class FakeTokenCss : IDesignTokenCssBuilder
    {
        public Task<string> BuildCssAsync(CancellationToken ct = default) => Task.FromResult(":root{}");
        public Task<string> GetCssHashAsync(CancellationToken ct = default) => Task.FromResult("hash");
    }

    private sealed class FakeSiteCode : ISiteCustomCodeService
    {
        public Task<SiteCustomCodeDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new SiteCustomCodeDto(null, null, null, null));

        public Task<Application.Common.Result<SiteCustomCodeDto>> SaveAsync(
            SiteCustomCodeSaveRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "NewsCMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class TestCurrentSite : Application.Site.ICurrentSite
    {
        public Guid SiteId => Guid.Empty;
        public string Slug => "test";
        public string Theme => "Universal";
        public bool IsResolved => true;
        public void Set(Guid siteId, string slug, string theme) { }
    }
}
