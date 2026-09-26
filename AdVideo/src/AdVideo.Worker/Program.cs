using AdVideo.Core.Pipeline;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Worker.Jobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Cùng phép kiểm tra như bên API, và cùng lý do: appsettings.json có sẵn khoá này với giá trị
// rỗng, nên kiểm tra null đơn thuần sẽ cho nó lọt qua rồi nổ muộn hơn trong ruột Hangfire.
string connectionString =
    builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName) ?? string.Empty;

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"Thiếu chuỗi kết nối '{DependencyInjection.ConnectionStringName}'. Xem AdVideo/CONFIGURATION.md.");
}

builder.Services.AddAdVideoInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);

// Pipeline + hai HttpClient của nó nằm trong WorkerPipeline để test integration dùng chung đúng
// một chỗ đăng ký với tiến trình thật. Danh sách bước chép làm hai bản là cách chắc chắn để một
// bước mới chạy lần đầu trên production.
builder.Services.AddAdVideoPipeline();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        // Phải khớp từng chữ với cấu hình bên API: khác SchemaName là hai tiến trình nhìn vào hai
        // hàng đợi khác nhau trong cùng một DB — API đẩy việc vào, worker ngồi không, và không có
        // lỗi nào cả.
        SchemaName = "hangfire",
        PrepareSchemaIfNecessary = true,
        QueuePollInterval = TimeSpan.FromSeconds(5),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
    }));

builder.Services.AddHangfireServer(options =>
{
    // Mặc định của Hangfire là ProcessorCount * 5 — hợp lý cho job gửi email, tai hoạ cho job gọi
    // FFmpeg. Trên máy 8 nhân đó là 40 job cùng chạy, mỗi job một tiến trình ffmpeg ăn hết CPU và
    // vài trăm MB đĩa tạm: máy không chết hẳn, chỉ chậm tới mức mọi job đều chạm timeout.
    options.WorkerCount = builder.Configuration.GetValue("AdVideo:Worker:MaxConcurrentJobs", 2);

    // Tên có kèm máy để bảng Servers của Hangfire đọc được khi chạy nhiều worker.
    options.ServerName = $"advideo-{Environment.MachineName}";

    // Cho bước đang chạy thời gian dừng tử tế khi deploy: AdVideoJobRunner bắt
    // OperationCanceledException và trả job về hàng đợi. Cắt ngay lập tức thì job nằm lại ở trạng
    // thái "đang chạy" cho tới khi Hangfire hết hạn khoá.
    options.ShutdownTimeout = TimeSpan.FromMinutes(2);
});

WebApplication app = builder.Build();

// Ném ngay lúc khởi động nếu ffmpeg/ffprobe/font không có thật. Để nó nổ ở giữa job thì tiền
// dựng hình đã tiêu xong rồi mới phát hiện không ghép được video.
app.Services.GetRequiredService<IOptions<FfmpegOptions>>().Value.Validate();

await InitializeAsync(app);

// Worker KHÔNG mở endpoint nghiệp vụ nào. /healthz tồn tại để bộ giám sát biết tiến trình này còn
// sống và còn nói chuyện được với DB lẫn kho file.
app.MapGet("/healthz", HealthAsync).WithName("Health");

app.Run();

/// <summary>
/// Kiểm tra những thứ worker bắt buộc phải có để chạy được một job.
/// </summary>
/// <remarks>
/// Khác với /healthz của API: ở đây mất kho file là <b>unhealthy</b> chứ không phải degraded. API
/// mất kho file vẫn nhận được job mới; worker mất kho file thì mọi job nó nhận đều sẽ thất bại
/// sau khi đã gọi provider và đã tiêu tiền.
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
        await storage.ExistsAsync(Buckets.Work, "__healthz__", cancellationToken);
        checks["storage"] = "ok";
    }
    catch (Exception ex)
    {
        checks["storage"] = $"lỗi: {ex.Message}";
        healthy = false;
    }

    return healthy
        ? Results.Ok(new { status = "healthy", checks })
        : Results.Json(new { status = "unhealthy", checks }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

/// <summary>
/// Việc phải làm trước khi nhận job đầu tiên.
/// </summary>
/// <remarks>
/// <para>
/// <b>Worker KHÔNG chạy migration, kể cả ở Development.</b> API đã làm việc đó. Hai tiến trình
/// cùng gọi <c>MigrateAsync</c> lúc khởi động là một cuộc đua trên schema, và bên thua có thể
/// chết giữa chừng sau khi đã đổi một phần bảng.
/// </para>
/// <para>
/// <b>Bucket thì vẫn phải bảo đảm.</b> Tạo bucket là idempotent, và worker có thể khởi động trước
/// API — nếu chờ API tạo hộ thì job đầu tiên sẽ thất bại ở bước ghi file với một lỗi nói về
/// bucket không tồn tại chứ không nói về thứ tự khởi động.
/// </para>
/// </remarks>
static async Task InitializeAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();
    IServiceProvider sp = scope.ServiceProvider;

    IStorageService storage = sp.GetRequiredService<IStorageService>();

    foreach (string bucket in new[] { Buckets.Uploads, Buckets.Work, Buckets.Final, Buckets.Voice })
    {
        await storage.EnsureBucketAsync(bucket);
    }

    // In ra thứ tự bước lúc khởi động: một bước quên đăng ký sẽ bị pipeline bỏ qua trong im lặng,
    // và dòng log này là chỗ duy nhất phát hiện được điều đó mà không cần chạy thử một job.
    ILogger logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdVideo.Worker");

    string pipeline = string.Join(
        " → ",
        sp.GetServices<IPipelineStep>().OrderBy(s => s.Order).Select(s => $"{s.Order}. {s.DisplayName}"));

    logger.LogInformation("Pipeline: {Pipeline}", pipeline);
}
