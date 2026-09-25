using AdVideo.Api;
using AdVideo.Api.Auth;
using AdVideo.Api.Cli;
using AdVideo.Api.Contracts;
using AdVideo.Api.Endpoints;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using Hangfire;
using Hangfire.SqlServer;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Chuỗi rỗng chứ không phải null: appsettings.json có sẵn khoá này với giá trị rỗng để người mới
// thấy tên khoá, nên phép kiểm tra null đơn thuần sẽ cho nó lọt qua và lỗi nổ muộn hơn, trong
// ruột Hangfire, với một thông báo không nhắc gì tới cấu hình.
string connectionString =
    builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName) ?? string.Empty;

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"Thiếu chuỗi kết nối '{DependencyInjection.ConnectionStringName}'. Xem AdVideo/CONFIGURATION.md.");
}

builder.Services.AddAdVideoInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Apply(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

builder.Services
    .AddAuthentication(ApiKeyAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(ApiKeyAuthHandler.SchemeName, null);

builder.Services.AddAuthorization();

// API chỉ là CLIENT của Hangfire — nó đẩy việc vào hàng đợi, Worker mới chạy. Gọi AddHangfireServer
// ở đây sẽ biến tiến trình phục vụ HTTP thành một tiến trình vừa phục vụ HTTP vừa render video,
// và một job FFmpeg nặng sẽ làm chậm mọi request đang chờ.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        // Schema riêng: bảng của Hangfire nằm cạnh bảng nghiệp vụ trong cùng một DB, và trộn
        // chúng vào dbo khiến mọi lệnh liệt kê bảng trở nên khó đọc.
        SchemaName = "hangfire",
        PrepareSchemaIfNecessary = true,
        QueuePollInterval = TimeSpan.FromSeconds(5),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
    }));

WebApplication app = builder.Build();

// Chạy lệnh quản trị rồi thoát, không mở cổng nào. Phải đặt SAU Build() để dùng đúng DI và đúng
// key ring DataProtection của API — một công cụ riêng sẽ mã hoá key bằng khoá mà API không mở được.
if (AdminCommands.IsCommand(args))
{
    return await AdminCommands.RunAsync(args, app.Services);
}

await InitializeAsync(app);

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapAdVideoEndpoints();

app.MapGet("/healthz", HealthAsync)
    .AllowAnonymous()
    .WithName("Health");

app.Run();

return 0;

/// <summary>
/// Kiểm tra DB, kho file và hàng đợi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Worker chết là "degraded", không phải "unhealthy".</b> API vẫn nhận job được và job vẫn nằm
/// trong hàng đợi chờ worker sống lại — khác hẳn với mất DB, lúc đó không nhận được gì. Trả
/// unhealthy cho cả hai sẽ khiến bộ cân bằng tải gỡ API ra khỏi vòng phục vụ vì một lý do mà việc
/// gỡ nó không giải quyết được.
/// </para>
/// </remarks>
static async Task<IResult> HealthAsync(
    AdVideoDbContext db,
    IStorageService storage,
    CancellationToken cancellationToken)
{
    var checks = new Dictionary<string, string>(StringComparer.Ordinal);
    bool healthy = true;

    try
    {
        checks["database"] = await db.Database.CanConnectAsync(cancellationToken) ? "ok" : "không kết nối được";
        healthy &= checks["database"] == "ok";
    }
    catch (Exception ex)
    {
        checks["database"] = $"lỗi: {ex.Message}";
        healthy = false;
    }

    try
    {
        // Hỏi một object chắc chắn không tồn tại: câu trả lời "không có" vẫn chứng minh kho file
        // đang trả lời, mà không cần ghi gì lên đó.
        await storage.ExistsAsync(Buckets.Final, "__healthz__", cancellationToken);
        checks["storage"] = "ok";
    }
    catch (Exception ex)
    {
        checks["storage"] = $"lỗi: {ex.Message}";
        healthy = false;
    }

    try
    {
        IMonitoringApi monitoring = JobStorage.Current.GetMonitoringApi();
        int servers = monitoring.Servers().Count;

        checks["queue"] = servers > 0
            ? $"ok ({servers} worker)"
            : "không có worker nào đang chạy — job sẽ nằm chờ trong hàng đợi";
    }
    catch (Exception ex)
    {
        checks["queue"] = $"lỗi: {ex.Message}";
        healthy = false;
    }

    var response = new HealthResponse(healthy ? "healthy" : "unhealthy", checks);

    return healthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
}

/// <summary>
/// Việc phải làm trước khi nhận request đầu tiên.
/// </summary>
/// <remarks>
/// <b>Chỉ tự động migrate và seed ở môi trường Development.</b> Migrate lúc khởi động trên
/// production là một cuộc đua: hai instance API cùng khởi động sẽ cùng chạy migration, và cái
/// thua cuộc có thể chết giữa chừng sau khi đã đổi một phần schema. Trên production thì chạy
/// <c>dotnet run -- migrate</c> như một bước deploy riêng, có người nhìn.
/// </remarks>
static async Task InitializeAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();
    IServiceProvider sp = scope.ServiceProvider;

    if (app.Environment.IsDevelopment())
    {
        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

        await db.Database.MigrateAsync();
        await sp.GetRequiredService<SystemSettingSeeder>().SeedAsync();
        await sp.GetRequiredService<PromptTemplateSeeder>().SeedAsync();
    }

    IStorageService storage = sp.GetRequiredService<IStorageService>();

    foreach (string bucket in new[] { Buckets.Uploads, Buckets.Work, Buckets.Final, Buckets.Voice })
    {
        // Idempotent, và phải chạy ở cả hai host: worker ghi vào adv-work còn API đọc từ adv-final,
        // nên host nào khởi động trước cũng phải thấy đủ bucket.
        await storage.EnsureBucketAsync(bucket);
    }
}

/// <summary>Điểm neo để test integration dựng được host này bằng <c>WebApplicationFactory</c>.</summary>
public partial class Program;
