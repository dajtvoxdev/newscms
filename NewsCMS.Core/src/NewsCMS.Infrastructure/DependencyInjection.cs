using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Application.Analytics;
using NewsCMS.Application.Ar;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Content;
using NewsCMS.Application.Engagement;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Analytics;
using NewsCMS.Infrastructure.Ar;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Catalog;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Engagement;
using NewsCMS.Infrastructure.Jobs;
using NewsCMS.Infrastructure.KeoBia;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Settings;
using NewsCMS.Infrastructure.Site;
using NewsCMS.Infrastructure.Storage;
using NewsCMS.Application.Ai;
using NewsCMS.Infrastructure.Ai;
using NewsCMS.Infrastructure.Ai.Tools;
using tusdotnet.Stores;

namespace NewsCMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        // 1. DbContext
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(cfg.GetConnectionString("Default")));

        // 2. Identity
        services.AddIdentity<AppUser, AppRole>(o =>
        {
            o.Password.RequireDigit = true;
            o.Password.RequiredLength = 8;
            o.Password.RequireNonAlphanumeric = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // 3. File storage - upload trực tiếp lên thư mục wwwroot/uploads của server,
        //    URL trả về được lưu vào DB (Media.FilePath). Không phụ thuộc dịch vụ ngoài.
        var storageProvider = cfg["Storage:Provider"] ?? "Local";
        var urlPrefix = cfg["Storage:UrlPrefix"] ?? "/uploads";
        var publicBaseUrl = cfg["Storage:PublicBaseUrl"] ?? "";
        services.AddSingleton<IFileStorage>(sp =>
        {
            if (!string.Equals(storageProvider, "Local", StringComparison.OrdinalIgnoreCase))
                return new CloudFileStorage();

            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var storageRoot = ResolveStoragePath(env, cfg["Storage:LocalRoot"], Path.Combine(env.WebRootPath, "uploads"));
            var trashRoot = ResolveStoragePath(env, cfg["Storage:TrashRoot"], Path.Combine(env.ContentRootPath, "App_Data", "media-trash"));
            return new LocalFileStorage(storageRoot, trashRoot, urlPrefix, publicBaseUrl);
        });

        // Store cho upload resumable tus (file lớn: video, PDF nặng...). Tách khỏi
        // wwwroot/uploads — chỉ khi hoàn tất mới move vào storage chính thức.
        services.AddSingleton<TusDiskStore>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var tusRoot = ResolveStoragePath(env, cfg["Storage:TusStoreRoot"], Path.Combine(env.ContentRootPath, "App_Data", "tus-uploads"));
            Directory.CreateDirectory(tusRoot);
            return new TusDiskStore(tusRoot);
        });
        // Lock trong memory là đủ: app chạy 1 instance; DiskFileLock chỉ cần khi multi-instance.
        // Instance qua InMemoryFileLockProvider.Instance — class không có public ctor để DI new.
        services.AddSingleton<tusdotnet.Interfaces.ITusFileLockProvider>(tusdotnet.FileLocks.InMemoryFileLockProvider.Instance);
        services.AddHttpContextAccessor();

        // DataProtection - persist key ring to App_Data/keys
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "..", "App_Data", "keys")));

        // 4. Content helpers cho admin editor: HTML sanitizer + slug generator
        services.AddSingleton<ContentSanitizer>();
        services.AddSingleton<SlugHelper>();

        // 5. Application services (PostService, MenuService...) sẽ được register tại đây
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IVisitorTrackingService, VisitorTrackingService>();
        services.AddScoped<ICommitmentService, CommitmentService>();
        services.AddScoped<IArExperienceService, ArExperienceService>();
        services.AddScoped<IArScannerService, ArScannerService>();
        services.AddScoped<ISiteSettingService, SiteSettingService>();
        services.AddScoped<VisitorRetentionJob>();

        // Multi-tenant: site hiện tại theo request + resolver host→site (cache)
        services.AddMemoryCache();
        services.AddSingleton<SiteCacheSignal>();
        services.AddScoped<CurrentSiteAccessor>();
        services.AddScoped<ICurrentSite>(sp => sp.GetRequiredService<CurrentSiteAccessor>());
        services.AddScoped<ISiteResolver, SiteResolver>();
        // Base URL chính thức của site — dùng CHUNG cho canonical/og:url/og:image (PageRenderer)
        // và <loc> của sitemap + robots.txt, để hai bên không thể trỏ hai domain khác nhau.
        services.AddScoped<ISiteUrlResolver, SiteUrlResolver>();

        // Phase 2.5: Registry loại nội dung (Content Type Registry) — Scoped để cache ContentDetail theo request.
        services.AddScoped<IContentType, Builder.ContentTypes.PostContentType>();
        services.AddScoped<IContentType, Builder.ContentTypes.ProductContentType>();
        services.AddScoped<IContentType, Builder.ContentTypes.CategoryContentType>();
        services.AddScoped<IContentTypeRegistry, Builder.ContentTypes.ContentTypeRegistry>();

        // Site Builder (theme Universal): routing path→entity + render trang + CSS design token.
        services.AddScoped<IRouteRegistry, RouteRegistry>();
        // Tra "URL chi tiết → trang builder nào render nó". Phải đăng ký TRƯỚC PageRenderer về mặt
        // đọc hiểu, còn thứ tự thực tế không quan trọng (DI resolve theo nhu cầu, không có vòng).
        services.AddScoped<ITemplateResolver, TemplateResolver>();
        services.AddScoped<IPageRenderer, PageRenderer>();
        services.AddScoped<IDesignTokenCssBuilder, DesignTokenCssBuilder>();
        services.AddScoped<IBuilderPageService, BuilderPageService>();
        services.AddScoped<ISiteLayoutService, SiteLayoutService>();
        services.AddScoped<ISiteCustomCodeService, SiteCustomCodeService>();

        // Dynamic blocks (Phase 4): registry + renderer + built-in blocks.
        // Scoped, KHÔNG singleton: registry giữ chính instance của từng IDynamicBlock, mà mỗi block
        // giữ AppDbContext (scoped). Singleton ở đây sẽ captive DbContext của request đầu tiên.
        services.AddScoped<IDynamicBlockRegistry, DynamicBlockRegistry>();
        services.AddScoped<DynamicBlockRenderer>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.PostListBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.CategoryListBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.BreadcrumbBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.BannerSliderBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.ProductGridBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.CoffeeBeanGridBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.NewsGridBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.PdfFlipbookBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.GalleryBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.SiteMenuBlock>();

        // Phase 2 (v3): Khối trường dữ liệu cho template trang chi tiết (EntityScoped = true).
        services.AddScoped<IDynamicBlock, Builder.Blocks.EntityTitleBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.EntityImageBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.EntityContentBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.EntityMetaBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.ProductPriceBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.ProductGalleryBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.RelatedPostsBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.ProductStockBlock>();
        services.AddScoped<IDynamicBlock, Builder.Blocks.EntityExcerptBlock>();

        // Phase 5: Category builder + SEO + i18n + sitemap.
        services.AddScoped<IBuilderCategoryService, BuilderCategoryService>();
        services.AddScoped<ISeoMetaService, SeoMetaService>();
        services.AddScoped<IPageTranslationService, PageTranslationService>();
        services.AddScoped<ISitemapGenerator, SitemapGenerator>();
        services.AddScoped<ISeoHealthService, SeoHealthService>();

        // Phase 6: tạo site từ template (clone spec → site mới).
        services.AddScoped<ISiteTemplateService, SiteTemplateService>();

        // Phase 7: bề mặt thao tác site cho agent (MCP) + API key.
        services.AddScoped<ISiteBuilderApi, SiteBuilderApi>();
        services.AddScoped<ISiteApiKeyService, SiteApiKeyService>();

        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IBannerService, BannerService>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IVideoThumbnailService, VideoThumbnailService>();
        services.Configure<KeoBiaTelegramOptions>(cfg.GetSection(KeoBiaTelegramOptions.SectionName));
        services.AddScoped<IKeoBiaService, KeoBiaService>();
        services.AddScoped<KeoBiaAiTools>();
        services.AddHttpClient(KeoBiaService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NewsCMS-KeoBia-Avatar/1.0");
            c.MaxResponseContentBufferSize = 5L * 1024 * 1024;
        });
        services.AddScoped<IKeoBiaTelegramOidcService, KeoBiaTelegramOidcService>();
        services.AddHttpClient(KeoBiaTelegramOidcService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NewsCMS-KeoBia-OIDC/1.0");
        });
        services.AddScoped<IKeoBiaAnalysisService, KeoBiaAnalysisService>();
        services.AddScoped<IKeoBiaOpenFootballSyncService, OpenFootballWorldCupSyncService>();
        services.AddHttpClient(OpenFootballWorldCupSyncService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NewsCMS-KeoBia-OpenFootball/1.0");
            c.MaxResponseContentBufferSize = 2L * 1024 * 1024;
        });
        services.AddHostedService<OpenFootballWorldCupSyncWorker>();

        // Football-Data.org: single source of truth for World Cup schedule + results + bia.
        services.AddScoped<IKeoBiaFootballDataSyncService, FootballDataResultSyncService>();
        services.AddHttpClient(FootballDataResultSyncService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NewsCMS-KeoBia-FootballData/1.0");
            c.MaxResponseContentBufferSize = 4L * 1024 * 1024;
        });
        services.AddHostedService<FootballDataSyncWorker>();
        services.AddHostedService<KeoBiaAnalysisWarmupWorker>();

        // Dọn dẹp file tus hết hạn + session upload treo/cũ.
        services.AddHostedService<TusCleanupWorker>();

        // AI module
        services.AddScoped<IAiConnectionService, AiConnectionService>();
        services.AddScoped<IAiSkillService, AiSkillService>();
        services.AddScoped<IAiCompletionService, AiCompletionService>();
        services.AddScoped<IAiChatClient, OpenAiChatClient>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiTool, FirecrawlSearchTool>();
        services.AddScoped<IAiTool, FirecrawlScrapeTool>();
        services.AddScoped<IAiTool, NineRouterSearchTool>();
        services.AddScoped<IAiTool, NineRouterFetchTool>();
        services.AddScoped<IAiTool, ImageGenerationTool>();
        // Phase 7: trợ lý AI trong admin dựng được trang, dùng chung ISiteBuilderApi với MCP.
        services.AddScoped<IAiTool, SiteBuilderSchemaTool>();
        services.AddScoped<IAiTool, SiteBuilderApplyTool>();
        services.AddScoped<IAiTool, SiteBuilderPreviewTool>();
        services.AddHttpClient("ai-tools", c => c.Timeout = TimeSpan.FromSeconds(30));

        return services;
    }

    private static string ResolveStoragePath(IWebHostEnvironment env, string? configuredPath, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(fallbackPath);

        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var normalized = configuredPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (normalized.Equals("wwwroot", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(env.WebRootPath);

        var webRootPrefix = $"wwwroot{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(webRootPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(env.WebRootPath) ?? env.ContentRootPath, normalized));

        return Path.GetFullPath(Path.Combine(env.ContentRootPath, normalized));
    }
}
