using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;
using NewsCMS.Web.Middleware;
using NewsCMS.Web.Theming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddRazorPages();

builder.Services.AddHttpClient();

builder.Services.AddSingleton<SiteCacheSignal>();

builder.Services.AddScoped<ICurrentSite, CurrentSite>();

builder.Services.AddMemoryCache();

builder.Services.AddScoped<ISiteResolver, SiteResolver>();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddSingleton<IConfigureOptions<StaticFileOptions>>(sp =>
{
    return new ConfigureOptions<StaticFileOptions>(options =>
    {
        options.FileProvider = new PhysicalFileProvider(builder.Environment.WebRootPath);
        options.RequestPath = "/_content";
        options.OnPrepareResponse = (context, response) =>
        {
            response.Headers.Append("Cache-Control", "public, max-age=31536000");
        };
    });
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCookiePolicy();

app.UseRouting();

app.UseMiddleware<SiteResolveMiddleware>();   // resolve site/theme theo host — TRƯỚC routing để matcher policy thấy theme
app.UseMiddleware<MaintenanceMiddleware>(); // nếu site.IsActive = false: trả về trang maintenance HTML chung

app.UseMiddleware<RedirectMiddleware>();      // SEO 301/302

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
