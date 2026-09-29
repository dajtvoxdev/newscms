using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Jobs;
using NewsCMS.Infrastructure.ImageStudio.Providers;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;
using NewsCMS.Tests.VideoStudio;

namespace NewsCMS.Tests.ImageStudio;

/// <summary>
/// Xưởng ảnh chạy trên DB InMemory, storage thư mục tạm, DataProtection tạm và provider giả
/// (<see cref="ScriptedImageProvider"/> kịch bản được từng lần gọi, hoặc Fake vẽ gradient).
/// </summary>
public sealed class ImageStudioTestHarness : IDisposable
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _dbName = "imagestudio-" + Guid.NewGuid().ToString("N");

    public ImageStudioTestHarness()
    {
        Site = new AdVideoTestHarness.TestSite(Guid.NewGuid());
        DataProtection = new EphemeralDataProtectionProvider();
        StorageRoot = Path.Combine(Path.GetTempPath(), "ncms-imagestudio-" + Guid.NewGuid().ToString("N"));
        Storage = new LocalFileStorage(StorageRoot, Path.Combine(StorageRoot, "_trash"));
        Options = Microsoft.Extensions.Options.Options.Create(new ImageStudioOptions { EnableFakeProvider = true });
        Scripted = new ScriptedImageProvider();
        Queue = new ImageJobQueue();
        Db = NewContext();
    }

    public AdVideoTestHarness.TestSite Site { get; }
    public IDataProtectionProvider DataProtection { get; }
    public string StorageRoot { get; }
    public LocalFileStorage Storage { get; }
    public IOptions<ImageStudioOptions> Options { get; }
    public ScriptedImageProvider Scripted { get; }
    public ImageJobQueue Queue { get; }
    public AppDbContext Db { get; }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName, _root)
            .ConfigureWarnings(w => w
                .Ignore(InMemoryEventId.TransactionIgnoredWarning)
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new AppDbContext(options, Site);
    }

    /// <summary>Registry có Fake + provider kịch bản (đội lốt adapter OpenAI).</summary>
    public ImageProviderRegistry Registry() => new([new FakeImageProvider(), Scripted], Options);

    public ImageModelCredentials Credentials(AppDbContext? db = null) =>
        new(db ?? Db, DataProtection, NullLogger<ImageModelCredentials>.Instance);

    public ImageStudioService Service(AppDbContext? db = null)
    {
        db ??= Db;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:OptimizeImages"] = "false",
        }).Build();
        var media = new MediaService(db, Storage, NullLogger<MediaService>.Instance, config, new HttpContextAccessor(), Site);

        return new ImageStudioService(db, Registry(), Queue, media, Storage, NullLogger<ImageStudioService>.Instance);
    }

    public ImageJobRunner Runner(AppDbContext? db = null)
    {
        db ??= NewContext();
        return new ImageJobRunner(db, Credentials(db), Registry(), Storage, NullLogger<ImageJobRunner>.Instance) { RetryWait = TimeSpan.Zero };
    }

    public ImageModelService ModelService(AppDbContext? db = null)
    {
        db ??= Db;
        return new ImageModelService(db, Registry(), Credentials(db), Storage);
    }

    /// <summary>Bật Xưởng ảnh cho site hiện tại.</summary>
    public async Task EnableSiteAsync(int monthly = 0, int daily = 0, string? brandStyle = null)
    {
        Db.ImageStudioSiteSettings.Add(new ImageStudioSiteSettings
        {
            SiteId = Site.SiteId,
            Enabled = true,
            MonthlyImageQuota = monthly,
            PerUserDailyQuota = daily,
            BrandStyle = brandStyle,
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>Kết nối AI có key thật (mã hoá bằng DataProtection của harness).</summary>
    public async Task<Guid> AddConnectionAsync(string baseUrl = "https://api.example.com/v1", string? apiKey = "sk-test-key-1234567890")
    {
        var connection = new AiConnection
        {
            Name = "Gateway thử",
            Provider = "openai",
            BaseUrl = baseUrl,
            DefaultModel = "gpt-4o",
            ApiKeyEncrypted = apiKey is null ? string.Empty : DataProtection.CreateProtector("NewsCMS.Ai.ApiKey").Protect(apiKey),
            IsActive = true,
        };
        Db.AiConnections.Add(connection);
        await Db.SaveChangesAsync();
        return connection.Id;
    }

    /// <summary>Model dùng provider kịch bản (adapter OpenAI) — có kết nối thật với key.</summary>
    public async Task<ImageModel> AddScriptedModelAsync(decimal price = 0.04m, int maxVariants = 4, bool isActive = true)
    {
        Guid connectionId = await AddConnectionAsync();
        var model = new ImageModel
        {
            Name = "Model kịch bản",
            ConnectionId = connectionId,
            Adapter = ImageProviderAdapter.OpenAiImages,
            ModelId = "gpt-image-test",
            Capabilities = ImageCapabilities.TextToImage,
            SupportedSizes = "1024x1024\n1536x1024\n1024x1536",
            MaxVariants = maxVariants,
            OutputFormat = "png",
            PricePerImageUsd = price,
            IsActive = isActive,
        };
        Db.ImageModels.Add(model);
        await Db.SaveChangesAsync();
        return model;
    }

    public async Task<ImageModel> AddFakeModelAsync()
    {
        var model = new ImageModel
        {
            Name = "Model giả",
            Adapter = ImageProviderAdapter.Fake,
            ModelId = "fake",
            Capabilities = ImageCapabilities.TextToImage,
            SupportedSizes = "1:1\n16:9\n9:16",
            MaxVariants = 4,
            OutputFormat = "png",
            PricePerImageUsd = 0.01m,
            IsActive = true,
        };
        Db.ImageModels.Add(model);
        await Db.SaveChangesAsync();
        return model;
    }

    public void Dispose()
    {
        Db.Dispose();

        try
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
        catch (IOException)
        {
            // thư mục tạm
        }
    }

    public static byte[] Png(int width = 64, int height = 48)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        using var stream = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return stream.ToArray();
    }
}

/// <summary>
/// Provider đội lốt adapter OpenAI, trả kết quả theo kịch bản xếp sẵn (hết kịch bản thì trả ảnh PNG
/// hợp lệ). Ghi lại mọi request để test soi prompt và kích thước.
/// </summary>
public sealed class ScriptedImageProvider : IImageProvider
{
    private readonly Queue<ImageProviderResult> _script = new();
    private readonly object _lock = new();

    public List<ImageGenerateRequest> Requests { get; } = [];

    public ImageProviderAdapter Adapter => ImageProviderAdapter.OpenAiImages;

    public void Enqueue(params ImageProviderResult[] results)
    {
        lock (_lock)
        {
            foreach (ImageProviderResult r in results)
            {
                _script.Enqueue(r);
            }
        }
    }

    public Task<ImageProviderResult> GenerateAsync(ImageGenerateRequest request, CancellationToken ct = default)
    {
        lock (_lock)
        {
            Requests.Add(request);

            return Task.FromResult(_script.Count > 0
                ? _script.Dequeue()
                : ImageProviderResult.Success(new ImageData(ImageStudioTestHarness.Png(), "image/png"), 200, 5));
        }
    }

    public static ImageProviderResult Fail(string code, int? status, string message = "lỗi thử") =>
        ImageProviderResult.Failure(code, message, status, 5);
}

/// <summary>HttpClientFactory trả client đi qua handler ghi lại request (dùng cho test adapter).</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<(HttpRequestMessage Request, string? Body)> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Seen.Add((request, body));
        return _respond(request);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
