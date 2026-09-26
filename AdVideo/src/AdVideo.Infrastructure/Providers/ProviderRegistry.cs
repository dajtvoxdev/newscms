using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Providers.Declarative;
using AdVideo.Infrastructure.Providers.ElevenLabs;
using AdVideo.Infrastructure.Providers.Fal;
using AdVideo.Infrastructure.Providers.Veo;
using AdVideo.Infrastructure.Providers.VieNeu;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Dựng provider từ credential + capability trong DB, và chọn provider theo yêu cầu nghiệp vụ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provider được dựng MỖI LẦN GỌI, không đăng ký sẵn trong DI.</b> Capability và endpoint nằm
/// trong DB (D10) và có thể đổi lúc đang chạy; một instance singleton sẽ giữ mãi giá trị đọc được
/// lúc khởi động, nên đổi model trong DB vẫn phải restart — đúng thứ D10 muốn tránh. Giá phải trả
/// là một vài lượt cấp phát cho mỗi shot, không đáng kể so với một lần gọi API mất hàng chục giây.
/// </para>
/// <para>
/// <b>Provider giả thì ngược lại: đăng ký sẵn trong DI</b> và được nối vào danh sách ở đây. Nó
/// không có credential trong DB, và ở Sprint 1 nó là thứ duy nhất chạy được khi chưa có API key.
/// </para>
/// <para>
/// <b>Thiếu capability thì BỎ QUA provider đó.</b> Không đoán giá trị mặc định: một lưới thời
/// lượng đoán sai làm bước 5 khoá timeline sai, và lỗi sẽ lộ ra ở video thành phẩm chứ không phải
/// ở log.
/// </para>
/// </remarks>
public sealed class ProviderRegistry : IProviderRegistry
{
    /// <summary>Tên client cấu hình sẵn timeout + retry. Xem <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "advideo-provider";

    private readonly ICredentialStore _credentials;
    private readonly IDescriptorStore _descriptors;
    private readonly ISettingsStore _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMediaInspector _inspector;
    private readonly IEnumerable<IVideoProvider> _builtInVideoProviders;
    private readonly IEnumerable<ITtsProvider> _builtInTtsProviders;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProviderRegistry> _logger;

    public ProviderRegistry(
        ICredentialStore credentials,
        IDescriptorStore descriptors,
        ISettingsStore settings,
        IHttpClientFactory httpClientFactory,
        IMediaInspector inspector,
        IEnumerable<IVideoProvider> builtInVideoProviders,
        IEnumerable<ITtsProvider> builtInTtsProviders,
        ILoggerFactory loggerFactory,
        ILogger<ProviderRegistry> logger)
    {
        _credentials = credentials;
        _descriptors = descriptors;
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _inspector = inspector;
        _builtInVideoProviders = builtInVideoProviders;
        _builtInTtsProviders = builtInTtsProviders;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IVideoProvider>> GetVideoProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = new List<IVideoProvider>();

        IReadOnlyList<ResolvedCredential> credentials =
            await _credentials.ListActiveAsync(ProviderCategory.Video, cancellationToken: cancellationToken);

        foreach (ResolvedCredential credential in credentials)
        {
            VideoProviderCapability? capability =
                await _credentials.GetVideoCapabilityAsync(credential.Provider, cancellationToken);

            if (capability is null)
            {
                _logger.LogWarning(
                    "Provider video {Provider} có credential nhưng thiếu capability trong DB — bỏ qua.",
                    credential.Provider);

                continue;
            }

            IVideoProvider? provider = await CreateVideoProviderAsync(credential, capability, cancellationToken);

            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        AppendBuiltIn(providers, _builtInVideoProviders, p => p.Name);

        return providers;
    }

    public async Task<IReadOnlyList<ITtsProvider>> GetTtsProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = new List<ITtsProvider>();

        IReadOnlyList<ResolvedCredential> credentials =
            await _credentials.ListActiveAsync(ProviderCategory.TextToSpeech, cancellationToken: cancellationToken);

        foreach (ResolvedCredential credential in credentials)
        {
            TtsProviderCapability? capability =
                await _credentials.GetTtsCapabilityAsync(credential.Provider, cancellationToken);

            if (capability is null)
            {
                _logger.LogWarning(
                    "Engine TTS {Provider} có credential nhưng thiếu capability trong DB — bỏ qua.",
                    credential.Provider);

                continue;
            }

            ITtsProvider? provider = await CreateTtsProviderAsync(credential, capability, cancellationToken);

            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        AppendBuiltIn(providers, _builtInTtsProviders, p => p.Name);

        return providers;
    }

    public async Task<IVideoProvider?> FindVideoProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IVideoProvider> providers = await GetVideoProvidersAsync(cancellationToken);

        return providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ITtsProvider?> FindTtsProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ITtsProvider> providers = await GetTtsProvidersAsync(cancellationToken);

        return providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ProviderSelectionResult> SelectVideoProviderAsync(
        VideoRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        IReadOnlyList<IVideoProvider> providers = await GetVideoProvidersAsync(cancellationToken);

        if (providers.Count == 0)
        {
            return ProviderSelectionResult.Failure(
                ["Chưa có provider video nào được bật. Nạp credential vào DB trước khi tạo video."]);
        }

        IReadOnlyList<VideoProviderCapability> allCapabilities = providers.Select(p => p.Capability).ToList();

        // Provider mặc định được xét TRƯỚC, rồi mới tới thứ tự ưu tiên trong DB. Đây là chỗ
        // người vận hành lái toàn hệ thống sang provider khác bằng một dòng setting, không cần deploy.
        string? preferred = await _settings.GetStringAsync(SettingKeys.DefaultVideoProvider, cancellationToken);

        IEnumerable<IVideoProvider> ordered = string.IsNullOrWhiteSpace(preferred)
            ? providers
            : providers
                .OrderByDescending(p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase))
                .ThenBy(_ => 0);

        var reasons = new List<string>();
        var suggestions = new List<ProviderSuggestion>();

        foreach (IVideoProvider provider in ordered)
        {
            CapabilityCheckResult check = ProviderCapabilityValidator.Check(
                requirements,
                provider.Capability,
                allCapabilities);

            if (check.IsSatisfied)
            {
                foreach (string warning in check.Warnings)
                {
                    _logger.LogWarning("Chọn {Provider} kèm cảnh báo: {Warning}", provider.Name, warning);
                }

                return ProviderSelectionResult.Success(provider.Name);
            }

            reasons.AddRange(check.BlockingReasons);
            suggestions.AddRange(check.Suggestions);
        }

        // Gộp lý do của mọi provider: nói "Kling không hỗ trợ 16:9" mà giấu việc Veo cũng không
        // hỗ trợ thì người dùng sửa xong vẫn hỏng, và phải hỏi lại lần nữa.
        return ProviderSelectionResult.Failure(
            reasons.Distinct(StringComparer.Ordinal).ToList(),
            suggestions
                .GroupBy(s => s.ProviderName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.EstimatedCostUsd ?? decimal.MaxValue)
                .ToList());
    }

    public async Task<ProviderSelectionResult> SelectTtsProviderAsync(
        VideoTier tier,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ITtsProvider> providers = await GetTtsProvidersAsync(cancellationToken);

        if (providers.Count == 0)
        {
            return ProviderSelectionResult.Failure(
                ["Chưa có engine TTS nào được bật. Nạp credential vào DB trước khi tạo video."]);
        }

        // Tier Nháp chấp nhận engine không có mốc thời gian (VieNeu); bản thành phẩm thì không.
        // Xem TtsProviderCapability.HasWordTimings: thiếu mốc là bước 5 mù hoàn toàn.
        bool requiresTimings = tier != VideoTier.Draft;

        string settingKey = tier == VideoTier.Draft
            ? SettingKeys.DraftTtsProvider
            : SettingKeys.StandardTtsProvider;

        string? preferred = await _settings.GetStringAsync(settingKey, cancellationToken);
        var reasons = new List<string>();
        var usable = new List<ITtsProvider>();

        foreach (ITtsProvider provider in providers)
        {
            if (!provider.Capability.SupportedLanguages.Contains("vi", StringComparer.OrdinalIgnoreCase))
            {
                reasons.Add($"{provider.Name} không đọc được tiếng Việt.");

                continue;
            }

            if (requiresTimings && !provider.Capability.HasWordTimings)
            {
                reasons.Add(
                    $"{provider.Name} không trả mốc thời gian theo từ nên chỉ dùng được cho bản nháp.");

                continue;
            }

            // Giọng hết hạn (ElevenLabs Default ngừng phục vụ 31/12/2026) bị loại TRƯỚC khi gọi:
            // gọi rồi mới biết nghĩa là job chết giữa chừng và vẫn có thể đã bị tính tiền.
            if (provider.Capability.VoicesExpireAt is { } expiry && expiry <= DateTime.UtcNow)
            {
                reasons.Add(
                    $"Giọng của {provider.Name} đã ngừng phục vụ từ {expiry:dd/MM/yyyy}. Cập nhật voice id trong DB.");

                continue;
            }

            usable.Add(provider);
        }

        if (usable.Count == 0)
        {
            return ProviderSelectionResult.Failure(reasons.Distinct(StringComparer.Ordinal).ToList());
        }

        ITtsProvider chosen = usable
            .OrderByDescending(p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase))

            // Trong số còn lại: rẻ nhất trước. Mốc theo ký tự là điểm cộng vì phụ đề khớp từng chữ
            // dựa vào nó, nhưng không đáng để trả giá cao hơn nếu engine kia cũng có mốc theo từ.
            .ThenBy(p => p.Capability.CostPer1000CharsUsd)
            .ThenByDescending(p => p.Capability.HasCharacterTimings)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .First();

        return ProviderSelectionResult.Success(chosen.Name);
    }

    private static void AppendBuiltIn<T>(List<T> target, IEnumerable<T> builtIn, Func<T, string> nameOf)
    {
        foreach (T provider in builtIn)
        {
            // Credential trong DB thắng provider dựng sẵn cùng tên: khi có key thật thì bản thật
            // phải được dùng, không phải bản giả còn sót lại trong DI.
            if (!target.Any(existing => string.Equals(nameOf(existing), nameOf(provider), StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(provider);
            }
        }
    }

    /// <remarks>
    /// Thứ tự: descriptor đang bật → adapter viết tay → bỏ qua. Descriptor thắng adapter cùng tên,
    /// để chuyển một provider sang descriptor (P4) là bật một dòng DB, và tắt dòng đó là quay về
    /// adapter cũ — không deploy ở cả hai chiều.
    /// </remarks>
    private async Task<IVideoProvider?> CreateVideoProviderAsync(
        ResolvedCredential credential,
        VideoProviderCapability capability,
        CancellationToken cancellationToken)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

        if (await _descriptors.GetActiveAsync(credential.Provider, cancellationToken) is { } descriptor)
        {
            if (descriptor.Descriptor.Kind == Core.Providers.Descriptors.DescriptorKind.Video)
            {
                return new DeclarativeVideoProvider(
                    http, credential, capability, descriptor, _credentials, _loggerFactory.CreateLogger<DeclarativeVideoProvider>());
            }

            _logger.LogWarning(
                "Descriptor {Provider} là loại {Kind} nhưng credential là video — bỏ qua descriptor.",
                credential.Provider,
                descriptor.Descriptor.Kind);
        }

        return credential.Provider switch
        {
            ProviderNames.Veo => new VeoVideoProvider(
                http, credential, capability, _loggerFactory.CreateLogger<VeoVideoProvider>()),

            // Ba model này đi qua cùng một hàng đợi của fal.ai; khác biệt nằm ở model id và
            // capability, cả hai đều lấy từ DB. Thêm model thứ tư chỉ cần một dòng trong DB.
            ProviderNames.Kling or ProviderNames.Seedance or ProviderNames.Vidu => new FalQueueVideoProvider(
                http, credential, capability, _loggerFactory.CreateLogger<FalQueueVideoProvider>()),

            _ => LogUnknown(credential.Provider),
        };

        IVideoProvider? LogUnknown(string provider)
        {
            http.Dispose();

            _logger.LogWarning(
                "Không có adapter cũng không có descriptor đang bật cho provider video {Provider} — nạp bằng set-descriptor + activate-descriptor.",
                provider);

            return null;
        }
    }

    private async Task<ITtsProvider?> CreateTtsProviderAsync(
        ResolvedCredential credential,
        TtsProviderCapability capability,
        CancellationToken cancellationToken)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

        if (await _descriptors.GetActiveAsync(credential.Provider, cancellationToken) is { } descriptor)
        {
            if (descriptor.Descriptor.Kind == Core.Providers.Descriptors.DescriptorKind.Tts)
            {
                return new DeclarativeTtsProvider(
                    http, credential, capability, descriptor, _credentials, _inspector, _loggerFactory.CreateLogger<DeclarativeTtsProvider>());
            }

            _logger.LogWarning(
                "Descriptor {Provider} là loại {Kind} nhưng credential là TTS — bỏ qua descriptor.",
                credential.Provider,
                descriptor.Descriptor.Kind);
        }

        return credential.Provider switch
        {
            ProviderNames.ElevenLabs => new ElevenLabsTtsProvider(
                http, credential, capability, _loggerFactory.CreateLogger<ElevenLabsTtsProvider>()),

            ProviderNames.VieNeu => new VieNeuTtsProvider(
                http, credential, capability, _inspector, _loggerFactory.CreateLogger<VieNeuTtsProvider>()),

            _ => LogUnknown(credential.Provider),
        };

        ITtsProvider? LogUnknown(string provider)
        {
            http.Dispose();

            _logger.LogWarning(
                "Không có adapter cũng không có descriptor đang bật cho engine TTS {Provider} — nạp bằng set-descriptor + activate-descriptor.",
                provider);

            return null;
        }
    }
}
