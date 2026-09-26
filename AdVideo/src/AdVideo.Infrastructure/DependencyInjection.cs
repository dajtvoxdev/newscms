using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Caching;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Infrastructure.Persistence.Stores;
using AdVideo.Infrastructure.Persistence.Tenancy;
using AdVideo.Infrastructure.Providers;
using AdVideo.Infrastructure.Providers.Fake;
using AdVideo.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure;

/// <summary>
/// Cửa duy nhất để API và Worker nạp tầng hạ tầng.
/// </summary>
/// <remarks>
/// <para>
/// Một điểm vào thay vì để mỗi host tự đăng ký: hai host đăng ký lệch nhau là lỗi chỉ lộ ra ở
/// runtime, trên một trong hai tiến trình, và thường là tiến trình không ai đang nhìn.
/// </para>
/// <para>
/// <b><see cref="IApiKeyProtector"/> phải là SINGLETON.</b> Nó bọc key ring của DataProtection;
/// đăng ký scoped thì mỗi request dựng một provider mới, và trên một số cấu hình key ring điều đó
/// dẫn tới key được sinh lại — nghĩa là mọi API key đã mã hoá trong DB thành rác không giải mã
/// được. Đây là lỗi đã từng xảy ra ở dự án khác trong cùng máy chủ này.
/// </para>
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Tên chuỗi kết nối trong cấu hình.</summary>
    public const string ConnectionStringName = "AdVideoDb";

    /// <summary>Thư mục giữ key ring DataProtection. Bỏ trống thì dùng chỗ mặc định của nền tảng.</summary>
    public const string KeyRingPathSetting = "AdVideo:DataProtection:KeyRingPath";

    /// <param name="services">Container DI của host.</param>
    /// <param name="configuration">Cấu hình của host.</param>
    /// <param name="environmentName">
    /// Tên môi trường của host (<c>builder.Environment.EnvironmentName</c>). Bắt buộc truyền vì
    /// một số cấu hình bị cấm hẳn trên Production — xem <see cref="FakeProviderOptions.Enabled"/>.
    /// </param>
    public static IServiceCollection AddAdVideoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        AddPersistence(services, configuration);
        AddStores(services);
        AddStorage(services, configuration);
        AddMedia(services, configuration);
        AddProviders(services, configuration, environmentName);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Thiếu chuỗi kết nối '{ConnectionStringName}'. AdVideo dùng DB RIÊNG (AdVideoDb), " +
                "không dùng chung DB với hệ thống khác.");
        }

        services.AddDbContext<AdVideoDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsAssembly(typeof(AdVideoDbContext).Assembly.FullName);

                    // Retry tích hợp của EF chỉ bắt lỗi mạng tạm thời; nó không bọc transaction
                    // do mình tự mở, nên các store có transaction vẫn phải tự xử lý.
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
                }));

        services.AddScoped<ITenantContext, TenantContext>();

        // Singleton — xem ghi chú đầu lớp. Đăng ký scoped là cách đánh mất toàn bộ API key trong DB.
        IDataProtectionBuilder dataProtection = services.AddDataProtection()

            // BẮT BUỘC, và đây là chỗ dễ bỏ sót nhất của kiến trúc hai tiến trình: mặc định
            // DataProtection lấy đường dẫn content root làm "tên ứng dụng". API và Worker nằm ở
            // hai thư mục khác nhau nên chúng sinh ra hai khoá khác nhau — Worker sẽ giải mã
            // không nổi API key mà API vừa ghi vào DB, và thông báo lỗi sẽ nói về payload hỏng
            // chứ không nói gì về tên ứng dụng.
            .SetApplicationName("AdVideo");

        string? keyRingPath = configuration[KeyRingPathSetting];

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            // Mặc định key ring nằm trong hồ sơ người dùng (Windows) hoặc ~/.aspnet (Linux), và
            // một lần dọn thư mục là một lần mất toàn bộ key đã mã hoá — đã xảy ra ở dự án khác
            // trên chính máy chủ này. Trên production hãy trỏ vào một thư mục được sao lưu.
            Directory.CreateDirectory(keyRingPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.AddSingleton<IApiKeyProtector, DataProtectionApiKeyProtector>();

        services.AddScoped<SystemSettingSeeder>();
        services.AddScoped<PromptTemplateSeeder>();
    }

    private static void AddStores(IServiceCollection services)
    {
        services.AddMemoryCache();

        // Tín hiệu huỷ cache là SINGLETON trong khi store là scoped: một người vận hành sửa DB
        // trong request này phải làm hỏng cache của mọi request khác, kể cả trên worker.
        services.AddSingleton<StoreCacheSignal<DbSettingsStore>>();
        services.AddSingleton<StoreCacheSignal<DbCredentialStore>>();
        services.AddSingleton<StoreCacheSignal<DbPromptStore>>();

        services.AddScoped<ISettingsStore, DbSettingsStore>();
        services.AddScoped<ICredentialStore, DbCredentialStore>();
        services.AddScoped<IPromptStore, DbPromptStore>();
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        StorageOptions options = new();
        configuration.GetSection(StorageOptions.SectionName).Bind(options);

        // Chọn cài đặt lúc đăng ký chứ không phải lúc gọi: đổi provider lưu trữ giữa chừng là
        // đổi cả nơi file đã ghi nằm, không phải một quyết định của từng request.
        if (options.Provider == StorageProvider.Minio)
        {
            services.AddSingleton<IStorageService, MinioStorageService>();
        }
        else
        {
            services.AddSingleton<IStorageService, LocalDiskStorageService>();
        }
    }

    private static void AddMedia(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.SectionName));

        services.AddSingleton<IFfmpegRunner, FfmpegRunner>();
        services.AddSingleton<IMediaInspector, FfprobeInspector>();
        services.AddSingleton<IVideoComposer, FfmpegComposer>();
    }

    private static void AddProviders(IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        services.Configure<FakeProviderOptions>(configuration.GetSection(FakeProviderOptions.SectionName));

        services.AddHttpClient(ProviderRegistry.HttpClientName, client =>
            {
                // 10 phút cho MỘT request HTTP, không phải cho cả lần render. Mặc định 100 giây của
                // HttpClient đủ cho lệnh gửi và lệnh hỏi trạng thái, nhưng không đủ để tải một clip
                // 8 giây qua đường truyền chậm — và timeout ở bước tải nghĩa là mất một shot đã trả tiền.
                // Ngân sách thời gian của cả lần render do CancellationToken của bước gọi giữ,
                // lấy từ setting VideoProviderTimeoutSeconds trong DB.
                client.Timeout = TimeSpan.FromMinutes(10);
            })
            .AddPolicyHandler((provider, _) =>
                PollyPolicies.ProviderRetry(
                    provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(PollyPolicies))))

            // Handler dùng lại trong 5 phút thay vì mặc định 2 phút: provider video giữ kết nối
            // lâu (một lần gọi có thể mất hàng phút), còn DNS của họ thì hiếm khi đổi.
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        // Provider giả được đăng ký sẵn vì nó không có credential trong DB. Đây là thứ duy nhất
        // chạy được ở Sprint 1 khi chưa có API key — và là lý do toàn bộ pipeline test được với
        // chi phí bằng 0. ProviderRegistry sẽ bỏ qua nó nếu có credential thật cùng tên.
        FakeProviderOptions fakeOptions = new();
        configuration.GetSection(FakeProviderOptions.SectionName).Bind(fakeOptions);

        // Chết lúc khởi động, không phải cảnh báo trong log: provider giả trên production là
        // khách trả tiền nhận về video testsrc2 mà job báo thành công.
        if (fakeOptions.Enabled && string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{FakeProviderOptions.SectionName}:Enabled = true trên môi trường Production. " +
                "Provider giả không bao giờ được chạy ở production — tắt nó, hoặc nếu đây là stack dev " +
                "thì đặt ASPNETCORE_ENVIRONMENT khác Production.");
        }

        if (fakeOptions.Enabled)
        {
            services.AddSingleton<IVideoProvider, FakeVideoProvider>();
            services.AddSingleton<ITtsProvider, FakeTtsProvider>();
        }

        services.AddScoped<IProviderRegistry, ProviderRegistry>();
    }
}
