using System.Globalization;
using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Media;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.Declarative;

/// <summary>
/// Engine TTS chạy bằng descriptor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mốc thời gian là thứ quyết định, không phải giá</b> (kế hoạch provider khai báo, mục 1.5):
/// mốc chảy vào bậc thời lượng shot — tức là vào tiền. Nên mốc chỉ đọc từ đúng chỗ descriptor khai,
/// ghép ký tự thành từ bằng CÙNG hàm với adapter viết tay (<see cref="WordTimingBuilder"/>), và
/// ba mảng lệch độ dài thì cắt về mảng ngắn nhất thay vì đoán.
/// </para>
/// <para>
/// Không có alignment thì đo độ dài audio bằng ffprobe — engine đó chỉ dùng được cho tier Nháp
/// (registry loại nó khỏi tier Thành phẩm qua <c>capability.hasWordTimings</c>).
/// </para>
/// </remarks>
public sealed class DeclarativeTtsProvider : ITtsProvider
{
    private readonly ProviderDescriptor _descriptor;
    private readonly string _sha256;
    private readonly ResolvedCredential _credential;
    private readonly HttpClient _http;
    private readonly IMediaInspector _inspector;
    private readonly ILogger _logger;
    private readonly DescriptorHttpExecutor _executor;

    public DeclarativeTtsProvider(
        HttpClient http,
        ResolvedCredential credential,
        TtsProviderCapability capability,
        ActiveDescriptor descriptor,
        ICredentialStore credentials,
        IMediaInspector inspector,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _descriptor = descriptor.Descriptor;
        _sha256 = descriptor.Sha256;
        _credential = credential;
        _http = http;
        _inspector = inspector;
        _logger = logger;
        _executor = new DescriptorHttpExecutor(http, _descriptor, credential, credentials, logger, delay);

        Capability = capability;
    }

    public string Name => _credential.Provider;

    public TtsProviderCapability Capability { get; }

    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_descriptor.Constraints?.MaxInputChars is { } maxChars && request.Text.Length > maxChars)
        {
            return Failure(VideoFailureKind.ContentRejected, $"Lời thoại dài {request.Text.Length} ký tự, {Name} chỉ nhận tối đa {maxChars} mỗi lần gọi.");
        }

        Dictionary<string, JsonNode?> variables = BuildVariables(_descriptor, _credential.ModelId, Capability, request);

        DescriptorExecution execution = await _executor.ExecuteAsync(new TemplateContext(variables, _descriptor.ValueMaps), cancellationToken);

        if (!execution.IsSuccess)
        {
            return Failure(execution.FailureKind, execution.FailureReason!, execution.RawError, execution.RetryAfterSeconds);
        }

        (byte[]? audio, string? audioFailure) = await ReadAudioAsync(execution, cancellationToken);

        if (audio is not { Length: > 0 })
        {
            return Failure(VideoFailureKind.Unknown, audioFailure ?? $"{Name} báo xong nhưng không có audio.");
        }

        IReadOnlyList<CharacterTiming> characters = [];
        IReadOnlyList<WordTiming> words = [];

        if (_descriptor.Result.Alignment is { } alignment)
        {
            (characters, words) = ReadAlignment(alignment, execution.ResultJson);

            if (characters.Count == 0 && words.Count == 0)
            {
                // Khai có mốc mà không đọc được mốc: đúng trường hợp nguy hiểm nhất — thành công
                // mà bước 5 mù. Fail ở đây, đừng để TtsStep phát hiện muộn.
                return Failure(VideoFailureKind.Unknown, $"{Name} trả audio nhưng không đọc được mốc thời gian theo result.alignment.");
            }
        }

        double duration = words.Count > 0 ? words[^1].EndSeconds
            : characters.Count > 0 ? characters[^1].EndSeconds
            : await MeasureDurationAsync(audio, request.JobId, cancellationToken);

        if (duration <= 0)
        {
            return Failure(VideoFailureKind.ProviderUnavailable, $"Không đo được độ dài audio của {Name}.");
        }

        int billed = _descriptor.Result.BilledCharactersHeader is { } header
            && execution.SubmitHeaders.TryGetValue(header, out string? rawBilled)
            && int.TryParse(rawBilled, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBilled)
                ? parsedBilled
                : request.Text.Length;

        return new TtsResult
        {
            IsSuccess = true,
            AudioBytes = audio,
            AudioContentType = _descriptor.Result.AudioContentType,
            AudioDurationSeconds = duration,
            WordTimings = words,
            CharacterTimings = characters,
            ProviderRequestId = execution.ProviderRequestId,
            BilledCharacterCount = billed,
            ReportedCostUsd = _descriptor.Result.ReportedCostPath is { } costPath
                ? JsonPathReader.ReadDecimal(execution.ResultJson, costPath)
                : null,
            EstimatedCostUsd = _descriptor.Cost is { } cost
                ? DescriptorCostCalculator.Calculate(cost, new DescriptorUsage(variables, Characters: billed))
                : null,
            DescriptorSha256 = _sha256,
        };
    }

    /// <inheritdoc cref="DeclarativeVideoProvider.PingAsync"/>
    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>Dịch request của pipeline thành biến mẫu. Công khai cho <c>test-descriptor</c>.</summary>
    public static Dictionary<string, JsonNode?> BuildVariables(
        ProviderDescriptor descriptor,
        string? credentialModelId,
        TtsProviderCapability capability,
        TtsRequest request)
    {
        var variables = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        foreach ((string key, JsonNode? value) in descriptor.Defaults ?? [])
        {
            variables[key] = value?.DeepClone();
        }

        bool continuation = capability.SupportsProsodyContinuation;

        variables[DescriptorVariables.ModelId] = string.IsNullOrWhiteSpace(credentialModelId) ? capability.ModelId : credentialModelId;
        variables[DescriptorVariables.JobId] = JsonTemplateRenderer.ToNode(request.JobId);
        variables[DescriptorVariables.Text] = request.Text;
        variables[DescriptorVariables.VoiceId] = request.VoiceId;
        variables[DescriptorVariables.Speed] = decimal.ToDouble(request.Speed <= 0 ? 1.0m : request.Speed);
        variables[DescriptorVariables.PreviousText] = continuation && !string.IsNullOrWhiteSpace(request.PreviousText) ? request.PreviousText : null;
        variables[DescriptorVariables.PreviousRequestId] = continuation && !string.IsNullOrWhiteSpace(request.PreviousRequestId) ? request.PreviousRequestId : null;

        return variables;
    }

    private async Task<(byte[]? Audio, string? Failure)> ReadAudioAsync(DescriptorExecution execution, CancellationToken cancellationToken)
    {
        if (execution.ContentBytes is { Length: > 0 } content)
        {
            return (content, null);
        }

        if (_descriptor.Result.AudioBase64Path is { } base64Path
            && JsonPathReader.ReadString(execution.ResultJson, base64Path) is { } base64)
        {
            try
            {
                return (Convert.FromBase64String(base64), null);
            }
            catch (FormatException)
            {
                return (null, $"{Name}: {base64Path} không phải base64 hợp lệ.");
            }
        }

        if (_descriptor.Result.AudioUrlPath is { } urlPath
            && JsonPathReader.ReadString(execution.ResultJson, urlPath) is { } url
            && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            // Link audio do provider trả về: tải KHÔNG kèm key (request mới, không header), vẫn
            // qua allowlist của client.
            return (await _http.GetByteArrayAsync(uri, cancellationToken), null);
        }

        return (null, null);
    }

    private static (IReadOnlyList<CharacterTiming> Characters, IReadOnlyList<WordTiming> Words) ReadAlignment(
        DescriptorAlignment alignment,
        JsonNode? json)
    {
        IReadOnlyList<string>? texts = JsonPathReader.ReadStringArray(json, alignment.TextPath);
        IReadOnlyList<double>? starts = JsonPathReader.ReadNumberArray(json, alignment.StartsPath);
        IReadOnlyList<double>? ends = JsonPathReader.ReadNumberArray(json, alignment.EndsPath);

        if (texts is null || starts is null || ends is null)
        {
            return ([], []);
        }

        // Chia chứ không nhân 0.001: 350 / 1000.0 ra đúng 0.35, còn 350 * 0.001 ra 0.35000000000000003.
        double divisor = alignment.TimeUnit == DescriptorTimeUnit.Milliseconds ? 1000.0 : 1.0;

        // Ba mảng song song: cắt về mảng NGẮN NHẤT thay vì tin mảng chữ. Lệch độ dài là chuyện không
        // nên xảy ra, nhưng nếu xảy ra thì cắt bớt vẫn hơn IndexOutOfRange giữa job.
        int count = Math.Min(texts.Count, Math.Min(starts.Count, ends.Count));

        if (alignment.Format == DescriptorAlignmentFormat.Words)
        {
            var words = new List<WordTiming>(count);

            for (int i = 0; i < count; i++)
            {
                words.Add(new WordTiming(texts[i], starts[i] / divisor, ends[i] / divisor));
            }

            return ([], words);
        }

        var characters = new List<CharacterTiming>(count);

        for (int i = 0; i < count; i++)
        {
            characters.Add(new CharacterTiming(
                string.IsNullOrEmpty(texts[i]) ? ' ' : texts[i][0],
                starts[i] / divisor,
                ends[i] / divisor));
        }

        return (characters, WordTimingBuilder.FromCharacters(characters));
    }

    private async Task<double> MeasureDurationAsync(byte[] audio, Guid jobId, CancellationToken cancellationToken)
    {
        string extension = _descriptor.Result.AudioContentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".wav";
        string path = Path.Combine(Path.GetTempPath(), $"advideo-{Name}-{jobId:N}-{Guid.NewGuid():N}{extension}");

        try
        {
            await File.WriteAllBytesAsync(path, audio, cancellationToken);

            return (await _inspector.ProbeAsync(path, cancellationToken: cancellationToken)).DurationSeconds;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Không xoá được file tạm {Path}.", path);
            }
        }
    }

    private TtsResult Failure(VideoFailureKind kind, string reason, string? rawError = null, int? retryAfter = null) => new()
    {
        IsSuccess = false,
        FailureKind = kind,
        FailureReason = reason,
        RawError = rawError,
        RetryAfterSeconds = retryAfter,
        DescriptorSha256 = _sha256,
    };
}
