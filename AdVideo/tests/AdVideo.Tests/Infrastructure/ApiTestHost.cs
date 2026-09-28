using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdVideo.Api;
using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Infrastructure.Persistence.Tenancy;
using AdVideo.Infrastructure.Storage;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Chạy đúng <c>Program</c> của AdVideo.Api trong tiến trình test: Sqlite, kho file trên đĩa tạm,
/// provider giả, Hangfire giả.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dựng host thật, không gọi thẳng hàm endpoint.</b> Những gì hay sai ở tầng này nằm NGOÀI hàm
/// endpoint: scheme xác thực nào gắn vào nhóm route nào, policy nào đòi claim nào, JSON snake_case
/// có được áp cho body hay không. Gọi thẳng hàm thì cả ba thứ đó không được kiểm.
/// </para>
/// <para>
/// <b>Hangfire bị thay</b> bằng <see cref="RecordingJobClient"/>: API chỉ là client của hàng đợi, và
/// thứ cần kiểm ở đây là "có đẩy việc hay không", không phải worker chạy việc ra sao — việc đó
/// thuộc về <see cref="PipelineTestHost"/>.
/// </para>
/// <para>
/// Môi trường <c>Testing</c>: không phải Development nên <c>Program</c> không tự migrate (migration
/// sinh cho SQL Server, Sqlite không đọc được), không phải Production nên được bật provider giả.
/// </para>
/// </remarks>
public sealed class ApiTestHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    private ApiTestHost(WebApplicationFactory<Program> factory, string root, RecordingJobClient jobs, List<string> log)
    {
        _factory = factory;
        Root = root;
        Jobs = jobs;
        Log = log;
    }

    /// <summary>Mọi dòng log của host. Endpoint trả 500 thì lý do nằm ở đây, không nằm trong thân phản hồi.</summary>
    public List<string> Log { get; }

    /// <summary>Các dòng log mức lỗi, gộp lại để đính vào thông báo assert.</summary>
    public string Errors
    {
        get
        {
            lock (Log)
            {
                return string.Join(Environment.NewLine, Log.Where(l => l.StartsWith("Error", StringComparison.Ordinal) || l.StartsWith("Critical", StringComparison.Ordinal)));
            }
        }
    }

    /// <summary>Thư mục tạm chứa DB, kho file và key ring.</summary>
    public string Root { get; }

    /// <summary>Việc API đã đẩy vào hàng đợi.</summary>
    public RecordingJobClient Jobs { get; }

    public IServiceProvider Services => _factory.Services;

    public static async Task<ApiTestHost> StartAsync(IDictionary<string, string?>? extraSettings = null)
    {
        string root = Path.Combine(Path.GetTempPath(), $"advideo-api-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        string databasePath = Path.Combine(root, "advideo.db");
        var jobs = new RecordingJobClient();
        var log = new List<string>();

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Program.cs ném nếu thiếu. Không bao giờ được dùng: DbContextOptions bị thay bằng Sqlite.
            ["ConnectionStrings:AdVideoDb"] = "Server=khong-dung-toi;Database=khong-dung-toi;",
            ["AdVideo:Storage:Provider"] = nameof(StorageProvider.LocalDisk),
            ["AdVideo:Storage:LocalRoot"] = Path.Combine(root, "storage"),
            ["AdVideo:DataProtection:KeyRingPath"] = Path.Combine(root, "keyring"),
            ["AdVideo:Ffmpeg:FfmpegPath"] = ExternalTools.FfmpegPath,
            ["AdVideo:Ffmpeg:FfprobePath"] = ExternalTools.FfprobePath,
            ["AdVideo:Ffmpeg:FontFile"] = ExternalTools.FontFile,
            ["AdVideo:FakeProviders:Enabled"] = "true",
            ["AdVideo:FakeProviders:LatencyMs"] = "0",
        };

        if (extraSettings is not null)
        {
            foreach (KeyValuePair<string, string?> pair in extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // UseSetting chứ không ConfigureAppConfiguration: Program.cs đọc chuỗi kết nối và
                // cấu hình storage NGAY ở top-level, trước Build(), và chỉ host setting mới tới kịp.
                foreach (KeyValuePair<string, string?> pair in settings)
                {
                    builder.UseSetting(pair.Key, pair.Value);
                }

                builder.ConfigureLogging(logging => logging.AddProvider(new ListLoggerProvider(log)));

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AdVideoDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.AddDbContext<AdVideoDbContext>(options => options
                        .UseSqlite($"Data Source={databasePath}")
                        .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, SqliteDecimalModelCustomizer>());

                    services.RemoveAll<IBackgroundJobClient>();
                    services.AddSingleton<IBackgroundJobClient>(jobs);

                    services.RemoveAll<JobStorage>();
                    services.AddSingleton<JobStorage, UnavailableJobStorage>();
                });
            });

        var host = new ApiTestHost(factory, root, jobs, log);

        await host.InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            await db.Database.EnsureCreatedAsync();

            await sp.GetRequiredService<SystemSettingSeeder>().SeedAsync();
            await sp.GetRequiredService<PromptTemplateSeeder>().SeedAsync();
        });

        return host;
    }

    /// <summary>Client không mang key nào.</summary>
    public HttpClient CreateAnonymousClient() => _factory.CreateClient();

    /// <summary>Client mang API key của một tenant.</summary>
    public HttpClient CreateTenantClient(string apiKey)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AdVideo-Key", apiKey);

        return client;
    }

    /// <summary>Tạo tenant trực tiếp trong DB, trả về (id, api key).</summary>
    public async Task<(Guid TenantId, string ApiKey)> CreateTenantAsync(string name = "Tenant test", bool isActive = true)
    {
        string apiKey = ApiKeyHasher.Generate();

        var tenant = new Tenant
        {
            Name = name,
            ApiKeyPrefix = ApiKeyHasher.LookupPrefix(apiKey),
            ApiKeyHash = ApiKeyHasher.Hash(apiKey),
            IsActive = isActive,
        };

        await InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        });

        return (tenant.Id, apiKey);
    }

    /// <summary>Mở scope DI với filter tenant tắt — để test đọc/ghi dữ liệu của mọi tenant.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().BypassFilters = true;

        return await work(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await InScopeAsync<object?>(async sp =>
        {
            await work(sp);
            return null;
        });
    }

    /// <summary>Đọc thân JSON snake_case của phản hồi.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>POST JSON kèm <c>Idempotency-Key</c>.</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string url, string json, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();

        // Sqlite giữ file qua pool kết nối; không xả pool thì Windows không cho xoá thư mục.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Rác trong thư mục tạm không đáng để làm đỏ một test đã chạy xong.
        }
    }
}

/// <summary>Hàng đợi giả: ghi lại việc được đẩy vào, không chạy gì cả.</summary>
public sealed class RecordingJobClient : IBackgroundJobClient
{
    private readonly List<Job> _created = [];

    public IReadOnlyList<Job> Created
    {
        get
        {
            lock (_created)
            {
                return _created.ToList();
            }
        }
    }

    public string Create(Job job, IState state)
    {
        lock (_created)
        {
            _created.Add(job);

            return _created.Count.ToString(CultureInfo.InvariantCulture);
        }
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>
/// Kho Hangfire không dùng được. Chặn đường mặc định dựng <c>SqlServerStorage</c> — thứ sẽ mở kết
/// nối tới một máy chủ không tồn tại ngay khi ai đó resolve <see cref="JobStorage"/>.
/// </summary>
internal sealed class UnavailableJobStorage : JobStorage
{
    public override IMonitoringApi GetMonitoringApi() =>
        throw new NotSupportedException("Test API không có hàng đợi thật.");

    public override IStorageConnection GetConnection() =>
        throw new NotSupportedException("Test API không có hàng đợi thật.");
}
