using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Qc;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Infrastructure.Persistence.Tenancy;
using AdVideo.Infrastructure.Providers.Fake;
using AdVideo.Infrastructure.Storage;
using AdVideo.Worker.Jobs;
using AdVideo.Worker.Steps;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Một bản AdVideo chạy được trong tiến trình test: DB Sqlite, kho file trên đĩa tạm,
/// provider giả, FFmpeg thật.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sqlite chứ không phải InMemory.</b> InMemory không áp unique index, nên nó cho qua đúng
/// những lỗi mà SQL Server thật bắt được — khoá idempotency trùng, hai shot cùng chỉ số. Một
/// test xanh trên InMemory rồi đỏ trên SQL thật còn nguy hiểm hơn là không có test.
/// </para>
/// <para>
/// <b>Không chạy migration.</b> Migration của dự án này sinh cho SQL Server nên Sqlite không đọc
/// được; ở đây dựng schema từ model bằng <c>EnsureCreated</c>. Đổi lại, test này KHÔNG kiểm tra
/// migration — việc đó thuộc về một lần chạy thật trên SQL Server.
/// </para>
/// <para>
/// <b>Không có MinIO.</b> Kho file chạy bằng <c>LocalDiskStorageService</c> trong thư mục tạm.
/// Bắt cả bộ test phụ thuộc vào một container đang chạy là cách để nó bị tắt đi.
/// </para>
/// </remarks>
public sealed class PipelineTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PipelineTestHost(
        ServiceProvider services,
        string root,
        StubImageHandler images,
        List<string> log)
    {
        _services = services;
        Root = root;
        Images = images;
        Log = log;
    }

    /// <summary>Thư mục tạm chứa DB, kho file và key ring của lần chạy này.</summary>
    public string Root { get; }

    /// <summary>Máy chủ ảnh giả mà bước 1 gọi vào.</summary>
    public StubImageHandler Images { get; }

    /// <summary>Mọi dòng log từ worker, để test soi được những cảnh báo không làm job đỏ.</summary>
    public List<string> Log { get; }

    /// <summary>Tenant của mọi job trong lần chạy này.</summary>
    public Guid TenantId { get; } = Guid.NewGuid();

    /// <summary>Thư mục gốc của kho file.</summary>
    public string StorageRoot => Path.Combine(Root, "storage");

    /// <summary>
    /// Dựng host và tạo sẵn schema + dữ liệu hạt giống.
    /// </summary>
    /// <param name="configureFakes">
    /// Chỉnh hành vi của provider giả: bắt shot nào fail, fail kiểu gì, TTS có trả mốc thời gian
    /// hay không.
    /// </param>
    public static async Task<PipelineTestHost> StartAsync(
        Action<FakeProviderOptions>? configureFakes = null)
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"advideo-test-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // AddAdVideoInfrastructure ném nếu thiếu chuỗi kết nối. Giá trị này không bao giờ được
            // dùng: descriptor DbContextOptions bị thay bằng Sqlite ngay bên dưới.
            ["ConnectionStrings:AdVideoDb"] = "Server=khong-dung-toi;Database=khong-dung-toi;",

            ["AdVideo:Storage:Provider"] = nameof(StorageProvider.LocalDisk),
            ["AdVideo:Storage:LocalRoot"] = Path.Combine(root, "storage"),

            // Key ring riêng cho từng lần chạy. Dùng chỗ mặc định của nền tảng nghĩa là test ghi
            // vào key ring của máy dev — và một lần dọn dẹp sau đó sẽ làm hỏng mọi API key đã mã
            // hoá trong DB dev.
            ["AdVideo:DataProtection:KeyRingPath"] = Path.Combine(root, "keyring"),

            ["AdVideo:Ffmpeg:FfmpegPath"] = ExternalTools.FfmpegPath,
            ["AdVideo:Ffmpeg:FfprobePath"] = ExternalTools.FfprobePath,
            ["AdVideo:Ffmpeg:FontFile"] = ExternalTools.FontFile,
            ["AdVideo:Ffmpeg:TimeoutSeconds"] = "300",

            ["AdVideo:FakeProviders:Enabled"] = "true",
            ["AdVideo:FakeProviders:LatencyMs"] = "0",
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var log = new List<string>();
        var images = new StubImageHandler();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new ListLoggerProvider(log));
        });

        services.AddAdVideoInfrastructure(configuration);
        services.AddAdVideoPipeline();

        UseSqlite(services, Path.Combine(root, "advideo.db"));

        // Bước 1 gọi vào máy chủ ảnh giả; mọi client còn lại bị chặn hẳn.
        services.AddHttpClient(IngestStep.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => images);

        services.AddHttpClient(RenderShotsStep.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());

        services.AddHttpClient(AdVideo.Infrastructure.Providers.ProviderRegistry.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());

        if (configureFakes is not null)
        {
            services.Configure(configureFakes);
        }

        ServiceProvider provider = services.BuildServiceProvider();
        var host = new PipelineTestHost(provider, root, images, log);

        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();
            await db.Database.EnsureCreatedAsync();

            await sp.GetRequiredService<SystemSettingSeeder>().SeedAsync();
            await sp.GetRequiredService<PromptTemplateSeeder>().SeedAsync();
        });

        return host;
    }

    /// <summary>
    /// Ghi một job vào DB ở trạng thái chờ chạy.
    /// </summary>
    public async Task<Guid> CreateJobAsync(string briefJson, Action<AdVideoJob>? configure = null)
    {
        var job = new AdVideoJob
        {
            TenantId = TenantId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            BriefJson = briefJson,
            Status = JobStatus.Queued,
            Provider = ProviderNames.Fake,
            AspectRatio = AspectRatio.Portrait9x16,
            Tier = VideoTier.Standard,
            QueuedAt = DateTime.UtcNow,
        };

        configure?.Invoke(job);

        await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        });

        return job.Id;
    }

    /// <summary>
    /// Sửa job từ một scope khác, như API vẫn làm trong lúc worker đang chạy.
    /// </summary>
    /// <remarks>
    /// Phải là scope riêng: bản job mà bộ chạy đang theo dõi không tự thấy thay đổi của người
    /// khác, và đó chính là thứ cần kiểm — bộ chạy có đọc lại từ DB hay không.
    /// </remarks>
    public Task UpdateJobAsync(Guid jobId, Action<AdVideoJob> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();
            AdVideoJob job = await db.Jobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId);

            change(job);

            await db.SaveChangesAsync();
        });
    }

    /// <summary>Chạy job qua đúng bộ chạy mà worker thật dùng.</summary>
    public Task RunAsync(Guid jobId) =>
        InScopeAsync(sp => sp.GetRequiredService<IAdVideoJobRunner>().RunAsync(jobId));

    /// <summary>Đọc lại job, bỏ qua global filter theo tenant.</summary>
    public Task<AdVideoJob> ReadJobAsync(Guid jobId) =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();

            return await db.Jobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId);
        });

    /// <summary>Đọc các shot của job, theo thứ tự.</summary>
    public Task<List<Shot>> ReadShotsAsync(Guid jobId) =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();

            return await db.Shots.Where(s => s.JobId == jobId).OrderBy(s => s.Index).ToListAsync();
        });

    /// <summary>Đọc các hiện vật của job, bỏ qua global filter theo tenant.</summary>
    public Task<List<MediaAsset>> ReadAssetsAsync(Guid jobId) =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();

            return await db.MediaAssets
                .IgnoreQueryFilters()
                .Where(a => a.JobId == jobId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
        });

    /// <summary>Đọc sổ cái lời gọi provider của job.</summary>
    public Task<List<ProviderCall>> ReadCallsAsync(Guid jobId) =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AdVideoDbContext>();

            return await db.ProviderCalls
                .Where(c => c.JobId == jobId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();
        });

    /// <summary>Đường dẫn thật trên đĩa của một object trong kho.</summary>
    public string PathOf(string bucket, string objectKey) =>
        Path.Combine(StorageRoot, bucket, objectKey.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Chạy ffprobe thật lên một file trong kho.</summary>
    public Task<MediaProbeResult> ProbeAsync(string bucket, string objectKey, bool measureLoudness = false) =>
        InScopeAsync(sp => sp.GetRequiredService<IMediaInspector>().ProbeAsync(
            PathOf(bucket, objectKey), measureLoudness, detectBlackFrames: false));

    /// <summary>Mở một scope DI mới và chạy việc trong đó.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();

        // Runner tự đặt tenant cho mình; các truy vấn của test thì không đi qua runner nên phải
        // tự mở filter, nếu không mọi câu lệnh đều trả về rỗng một cách im lặng.
        scope.ServiceProvider.GetRequiredService<ITenantContext>().BypassFilters = true;

        return await work(scope.ServiceProvider);
    }

    /// <summary>Mở một scope DI mới và chạy việc trong đó.</summary>
    public async Task InScopeAsync(Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await InScopeAsync<object?>(async sp =>
        {
            await work(sp);
            return null;
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Trên Windows, ffmpeg vừa thoát vẫn có thể còn giữ handle vài chục mili-giây. Rác
            // trong %TEMP% không đáng để làm đỏ một test đã chạy xong.
        }
    }

    /// <summary>
    /// Thay SQL Server bằng Sqlite trên file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddAdVideoInfrastructure</c> gắn cứng <c>UseSqlServer</c> — đó là chủ ý: hai host đăng
    /// ký lệch nhau là lỗi chỉ lộ ra ở runtime. Nên ở đây phải gỡ descriptor rồi đăng ký lại,
    /// chứ không có cờ cấu hình nào để đổi.
    /// </para>
    /// <para>
    /// Dùng file chứ không dùng <c>:memory:</c>: Sqlite in-memory sống theo từng connection, mà
    /// mỗi <c>DbContext</c> trong pipeline mở connection riêng. Chung một file thì mọi scope nhìn
    /// thấy cùng một DB, đúng như với SQL Server.
    /// </para>
    /// </remarks>
    private static void UseSqlite(IServiceCollection services, string databasePath)
    {
        services.RemoveAll<DbContextOptions<AdVideoDbContext>>();
        services.RemoveAll<DbContextOptions>();

        services.AddDbContext<AdVideoDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
    }
}
