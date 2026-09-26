using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.Declarative;

/// <summary>
/// Provider video chạy bằng descriptor thay vì adapter viết tay.
/// </summary>
/// <remarks>
/// <para>
/// Phần "hiểu request của pipeline" nằm ở đây — dịch <see cref="VideoRequest"/> + capability thành
/// biến mẫu; phần "nói chuyện với provider" nằm ở <see cref="DescriptorHttpExecutor"/>. Hai quyết
/// định vẫn thuộc về code chứ không thuộc descriptor: ảnh tham chiếu chỉ gửi khi capability nhận
/// image-to-video, seed chỉ gửi khi capability tôn trọng seed.
/// </para>
/// <para>
/// <b><c>generate_audio</c> chỉ có giá trị (<c>false</c>) khi pipeline cần tắt tiếng gốc</b>; còn lại
/// là null nên khoá biến mất và provider dùng mặc định của nó — đúng như adapter fal cũ.
/// </para>
/// </remarks>
public sealed class DeclarativeVideoProvider : IVideoProvider
{
    private readonly ProviderDescriptor _descriptor;
    private readonly string _sha256;
    private readonly ResolvedCredential _credential;
    private readonly DescriptorHttpExecutor _executor;

    public DeclarativeVideoProvider(
        HttpClient http,
        ResolvedCredential credential,
        VideoProviderCapability capability,
        ActiveDescriptor descriptor,
        ICredentialStore credentials,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _descriptor = descriptor.Descriptor;
        _sha256 = descriptor.Sha256;
        _credential = credential;
        _executor = new DescriptorHttpExecutor(http, _descriptor, credential, credentials, logger, delay);

        Capability = capability;
    }

    public string Name => _credential.Provider;

    public VideoProviderCapability Capability { get; }

    public async Task<VideoResult> GenerateAsync(VideoRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_descriptor.Constraints?.MaxInputChars is { } maxChars && request.Prompt.Length > maxChars)
        {
            return Failure(VideoFailureKind.ContentRejected, $"Prompt dài {request.Prompt.Length} ký tự, {Name} chỉ nhận tối đa {maxChars}.");
        }

        IReadOnlyList<string> images = ReferenceImages(_descriptor, Capability, request);
        Dictionary<string, JsonNode?> variables = BuildVariables(_descriptor, _credential.ModelId, Capability, request, images);
        var context = new TemplateContext(variables, _descriptor.ValueMaps);

        DescriptorExecution execution = await _executor.ExecuteAsync(context, cancellationToken);

        if (!execution.IsSuccess)
        {
            return Failure(execution.FailureKind, execution.FailureReason!, execution.RawError, execution.RetryAfterSeconds, execution.ProviderRequestId);
        }

        byte[]? bytes = execution.ContentBytes;
        Uri? uri = execution.ProbedUrl;

        if (bytes is null && uri is null && _descriptor.Result.VideoBase64Path is { } base64Path
            && JsonPathReader.ReadString(execution.ResultJson, base64Path) is { } base64)
        {
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return Failure(VideoFailureKind.Unknown, $"{Name}: {base64Path} không phải base64 hợp lệ.", providerRequestId: execution.ProviderRequestId);
            }
        }

        if (bytes is null && uri is null && _descriptor.Result.VideoUrlPath is { } urlPath
            && JsonPathReader.ReadString(execution.ResultJson, urlPath) is { } url
            && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
        }

        if (bytes is not { Length: > 0 } && uri is null)
        {
            return Failure(
                VideoFailureKind.Unknown,
                $"{Name} báo xong nhưng không tìm thấy video trong kết quả.",
                execution.ResultJson?.ToJsonString(),
                providerRequestId: execution.ProviderRequestId);
        }

        return new VideoResult
        {
            IsSuccess = true,
            ProviderRequestId = execution.ProviderRequestId,
            VideoBytes = bytes,
            VideoUri = bytes is { Length: > 0 } ? null : uri,
            HasNativeAudio = Capability.GeneratesNativeAudio && !request.SuppressNativeAudio,
            MeasuredDurationSeconds = request.DurationSeconds,
            ReportedCostUsd = _descriptor.Result.ReportedCostPath is { } costPath
                ? JsonPathReader.ReadDecimal(execution.ResultJson, costPath)
                : null,
            EstimatedCostUsd = _descriptor.Cost is { } cost
                ? DescriptorCostCalculator.Calculate(cost, new DescriptorUsage(variables, Seconds: request.DurationSeconds, ReferenceImages: images.Count))
                : null,
            DescriptorSha256 = _sha256,
        };
    }

    /// <remarks>
    /// Descriptor không khai endpoint kiểm tra sức khoẻ, và gọi thử submit là tiêu tiền. Kiểm cấu
    /// hình bằng lệnh <c>test-descriptor</c>; kiểm key bằng một job nháp.
    /// </remarks>
    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>Ảnh tham chiếu thật sự gửi đi: chỉ khi capability nhận image-to-video, cắt theo giới hạn.</summary>
    public static IReadOnlyList<string> ReferenceImages(ProviderDescriptor descriptor, VideoProviderCapability capability, VideoRequest request)
    {
        if (!capability.SupportsImageToVideo || request.ReferenceImageUrls.Count == 0)
        {
            return [];
        }

        int limit = descriptor.Constraints?.MaxReferenceImages ?? Math.Max(1, capability.MaxReferenceImages);

        return request.ReferenceImageUrls.Take(limit).ToList();
    }

    /// <summary>
    /// Dịch request của pipeline thành biến mẫu. Công khai để <c>test-descriptor</c> chạy khô bằng
    /// ĐÚNG hàm này, không phải một bản sao.
    /// </summary>
    public static Dictionary<string, JsonNode?> BuildVariables(
        ProviderDescriptor descriptor,
        string? credentialModelId,
        VideoProviderCapability capability,
        VideoRequest request,
        IReadOnlyList<string> images)
    {
        var variables = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        foreach ((string key, JsonNode? value) in descriptor.Defaults ?? [])
        {
            variables[key] = value?.DeepClone();
        }

        variables[DescriptorVariables.ModelId] = string.IsNullOrWhiteSpace(credentialModelId) ? capability.ModelId : credentialModelId;
        variables[DescriptorVariables.JobId] = JsonTemplateRenderer.ToNode(request.JobId);
        variables[DescriptorVariables.ShotIndex] = request.ShotIndex;
        variables[DescriptorVariables.Prompt] = request.Prompt;
        variables[DescriptorVariables.NegativePrompt] = string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt;
        variables[DescriptorVariables.Seconds] = request.DurationSeconds;
        variables[DescriptorVariables.AspectRatio] = request.AspectRatio.ToString();
        variables[DescriptorVariables.ImageUrl] = images.Count > 0 ? images[0] : null;
        variables[DescriptorVariables.ImageUrls] = images.Count > 0 ? JsonTemplateRenderer.ToNode(images) : null;
        variables[DescriptorVariables.Seed] = request.Seed is { } seed && capability.SupportsSeed ? seed : null;
        variables[DescriptorVariables.GenerateAudio] = request.SuppressNativeAudio ? false : null;

        return variables;
    }

    private VideoResult Failure(
        VideoFailureKind kind,
        string reason,
        string? rawError = null,
        int? retryAfter = null,
        string? providerRequestId = null) => new()
    {
        IsSuccess = false,
        FailureKind = kind,
        FailureReason = reason,
        RawError = rawError,
        RetryAfterSeconds = retryAfter,
        ProviderRequestId = providerRequestId,
        DescriptorSha256 = _sha256,
    };
}
