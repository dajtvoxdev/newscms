using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing.Matching;
using NewsCMS.Application;
using NewsCMS.Infrastructure;
using NewsCMS.Shared.Theming;
using NewsCMS.Web.Authorization;
using NewsCMS.Web.Middleware;
using NewsCMS.Web.Theming;
using tusdotnet;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Static web assets (_content/<Theme>/...) chỉ được nạp tự động ở môi trường Development.
// App chạy Release qua restart-app.bat nên phải gọi tường minh, nếu không toàn bộ
// css/js/ảnh của theme sẽ 404.
builder.WebHost.UseStaticWebAssets();

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(ctx.HostingEnvironment.ContentRootPath, "logs", "newscms-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

// ---------------------------------------------------------------------------
// Theme modules
// ---------------------------------------------------------------------------
// Mỗi theme là 1 Razor Class Library chứa controller + view + wwwroot của một site.
// ConfigureServices chạy cho TẤT CẢ theme (admin dùng chung service của theme,
// ví dụ ITelegramBotClient của KeoBia2026); còn route thì được lọc theo host lúc
// runtime bởi ThemeEndpointMatcherPolicy.
var themeModules = new IThemeModule[]
{
    new NewsCMS.Theme.HaiLuuNguoc.ThemeStartup(),
    new NewsCMS.Theme.KeoBia2026.ThemeStartup(),
    new NewsCMS.Theme.PhuPhucYaka.ThemeStartup(),
    // Theme dựng site kéo-thả: catch-all {**path} render nội dung từ DB. Chỉ kích hoạt cho
    // site có DefaultTheme = "Universal" (ThemeEndpointMatcherPolicy lọc theo host) nên không
    // ảnh hưởng 3 theme RCL cũ ở trên.
    new NewsCMS.Theme.Universal.ThemeStartup(),
};

var themeAssemblies = themeModules
    .Select(m => m.GetType().Assembly)
    .Distinct()
    .ToArray();

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Không đặt PropertyNamingPolicy = null: giữ camelCase mặc định của web cho khớp với
        // toàn bộ JS phía admin (builder, media library, theme...). Đặt null sẽ trả PascalCase
        // khiến Site Builder đọc data.builderJson ra undefined và nạp canvas rỗng.
    })
    .ConfigureApplicationPartManager(apm => AddThemeApplicationParts(apm, themeAssemblies));

builder.Services.AddRazorPages();

// Cho phép antiforgery đọc token từ header "RequestVerificationToken" (bên cạnh form field).
// Cần cho các lời gọi fetch/AJAX trong admin (Builder sandbox, trợ lý AI...) gửi token qua header.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// View của theme: /Areas/{Theme}/Views/... (RCL) + /Themes/{Theme}/Views/... (nếu có).
builder.Services.Configure<RazorViewEngineOptions>(o =>
    o.ViewLocationExpanders.Add(new ThemeViewLocationExpander()));

// Nhiều theme cùng đăng ký pattern "" hoặc "san-pham" → policy loại endpoint sai theme.
builder.Services.AddSingleton<MatcherPolicy, ThemeEndpointMatcherPolicy>();

// Policy dạng "Content.ManagePost" được sinh động từ tên policy có dấu chấm.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddHttpClient();

// ---------------------------------------------------------------------------
// MCP server (Phase 7) — agent dựng site qua /mcp
// ---------------------------------------------------------------------------
// Tool chỉ là vỏ mỏng gọi xuống ISiteBuilderApi. Xác thực bằng SiteApiKey ở
// McpApiKeyMiddleware (chạy trước UseRouting) — middleware set ICurrentSite theo
// key nên global query filter của AppDbContext tự chặn thao tác chéo site.
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "newscms-site-builder",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithTools<NewsCMS.Web.Mcp.SiteBuilderMcpTools>();

foreach (var theme in themeModules)
    theme.ConfigureServices(builder.Services);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Seed theo yêu cầu: dotnet run -- --seed
// ---------------------------------------------------------------------------
// KHÔNG chạy tự động lúc khởi động vì DbSeeder gọi Database.MigrateAsync() — áp migration
// ngầm mỗi lần start là rủi ro với production. Chạy tường minh khi cần seed dữ liệu mới
// (permission mới, SiteTemplate mới...) rồi thoát, không phục vụ request.
if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    using var seedScope = app.Services.CreateScope();
    await NewsCMS.Infrastructure.Persistence.Seed.DbSeeder.SeedAsync(seedScope.ServiceProvider);
    Console.WriteLine("Seed hoàn tất.");
    return;
}

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
// Chạy sau Cloudflare Tunnel: TLS kết thúc ở Cloudflare, cloudflared gọi Kestrel
// bằng HTTP. Không dùng UseHttpsRedirection (sẽ tạo redirect loop qua tunnel);
// thay vào đó đọc X-Forwarded-* để Request.Scheme/IsHttps và link sinh ra đúng.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Không trỏ vào "/Home/Error": route public do theme tự đăng ký, không có
    // pattern {controller}/{action} nào bảo đảm tồn tại → handler sẽ 404 và trả
    // body rỗng. Ghi thẳng trang lỗi tối giản, chi tiết đã có trong Serilog.
    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(
            "<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\">" +
            "<title>Lỗi hệ thống</title></head><body style=\"font-family:system-ui;padding:40px\">" +
            "<h1>Đã có lỗi xảy ra</h1><p>Vui lòng thử lại sau ít phút.</p></body></html>");
    }));
    app.UseHsts();
}

app.UseSerilogRequestLogging();

var staticFileTypes = new FileExtensionContentTypeProvider();
staticFileTypes.Mappings[".mind"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileTypes,
    // Media (/uploads/**) có tên file GUID nên thường coi là bất biến — NHƯNG video thì KHÔNG:
    // VideoCompressionWorker ghi đè chính file .mp4 tại cùng URL sau khi nén (549MB HEVC →
    // 15MB H.264). Nếu khai báo immutable, trình duyệt đã cache bản gốc sẽ KHÔNG BAO GIỜ tải
    // lại, và phục vụ mãi bytes HEVC cũ (không phát được trên Chrome/Firefox) dù server đã
    // có bản H.264 đúng — đã xảy ra thật, người dùng "nén xong vẫn không xem được video".
    // Video dùng no-cache (vẫn cache bytes, nhưng luôn revalidate qua ETag): file không đổi
    // → 304 rỗng (rẻ), file vừa bị nén → 200 với bytes mới. Ảnh/pdf giữ immutable vì chỉ
    // được tối ưu một lần ngay lúc upload, không bao giờ bị ghi đè sau đó.
    // StaticFiles vẫn tự xử lý Range request (seek video).
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path;
        if (!path.StartsWithSegments("/uploads")) return;

        ctx.Context.Response.Headers.CacheControl = path.StartsWithSegments("/uploads/videos")
            ? "public,no-cache"
            : "public,max-age=31536000,immutable";
    }
});
app.UseCookiePolicy();

// PHẢI trước UseRouting: ThemeEndpointMatcherPolicy đọc theme từ HttpContext.Items
// ngay trong bước matching của UseRouting.
app.UseMiddleware<SiteResolveMiddleware>();   // host → site/theme; host lạ → 404
app.UseMiddleware<MaintenanceMiddleware>();   // site.IsActive = false → trang bảo trì 503
app.UseMiddleware<CspNonceMiddleware>();      // CSP nonce per-request cho theme Universal
app.UseMiddleware<RedirectMiddleware>();      // SEO 301/302
app.UseMiddleware<McpApiKeyMiddleware>();     // /mcp: API key → ICurrentSite + rate limit

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<AdminSiteScopeMiddleware>();   // cần User → sau UseAuthentication
app.UseMiddleware<VisitorTrackingMiddleware>();

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------
// Upload resumable tus cho file nặng (video 2GB, PDF lớn...): đăng ký TRƯỚC
// MapRazorPages, sau UseAuthentication/UseAuthorization/AdminSiteScope để hưởng
// cookie auth + tenant scoping /admin. Config factory chạy mỗi request.
app.MapTus(NewsCMS.Web.Endpoints.TusUploadEndpoint.Path,
    NewsCMS.Web.Endpoints.TusUploadEndpoint.BuildConfigurationAsync);

app.MapRazorPages();   // /admin/**, /Account/**, /Sitemap

// MCP endpoint cho agent. Đăng ký trước route catch-all của theme để không bị nuốt.
app.MapMcp("/mcp");

// Route mặc định cho các area (admin dùng Razor Pages, còn lại là controller của theme).
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Route public của từng theme. Đăng ký sau cùng vì có pattern catch-all
// ("{categorySlug}", "") sẽ nuốt mọi URL còn lại.
foreach (var theme in themeModules)
    theme.ConfigureRoutes(app);

app.Run();

// Nạp controller + view đã biên dịch của các theme RCL vào MVC.
// Idempotent: nếu assembly đã được auto-discovery thêm rồi thì bỏ qua, tránh
// đăng ký trùng controller (gây AmbiguousMatchException).
static void AddThemeApplicationParts(ApplicationPartManager apm, IEnumerable<Assembly> assemblies)
{
    foreach (var assembly in assemblies)
    {
        var alreadyRegistered = apm.ApplicationParts
            .OfType<AssemblyPart>()
            .Any(p => p.Assembly == assembly);

        if (alreadyRegistered) continue;

        foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
            apm.ApplicationParts.Add(part);
    }
}
