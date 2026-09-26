using System.Text.Json;
using System.Text.Json.Nodes;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;

namespace AdVideo.Infrastructure.Providers.Declarative;

/// <summary>Request mà descriptor sẽ gửi cho một request mẫu — không gọi mạng.</summary>
public sealed record DescriptorPreviewResult(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Chạy khô một descriptor: dựng submit request cho một request mẫu bằng ĐÚNG hàm dựng biến của
/// provider thật, và kiểm host với allowlist. Dùng cho lệnh <c>test-descriptor</c>.
/// </summary>
/// <remarks>
/// Key được thay bằng <c>****</c>: bản xem trước in ra màn hình và có thể bị dán vào ticket.
/// </remarks>
public static class DescriptorPreview
{
    public const string SampleImageUrl = "https://example.com/san-pham.jpg";

    public static DescriptorPreviewResult Render(ProviderDescriptor descriptor, ProviderHostAllowlist allowlist)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(allowlist);

        var warnings = new List<string>();
        Dictionary<string, JsonNode?> variables = descriptor.Kind == DescriptorKind.Video
            ? VideoVariables(descriptor)
            : TtsVariables(descriptor);

        variables[DescriptorVariables.SecretApiKey] = "****";

        var context = new TemplateContext(variables, descriptor.ValueMaps);
        string baseUrl = descriptor.Transport.BaseUrl.TrimEnd('/');

        if (allowlist.Explain(new Uri(baseUrl)) is { } blocked)
        {
            warnings.Add($"transport.baseUrl sẽ bị chặn: {blocked}");
        }

        string path = JsonTemplateRenderer.RenderString(descriptor.Submit.Path, context, asUrlPath: true)
            ?? throw new TemplateRenderException("submit.path thiếu biến với request mẫu.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (descriptor.Transport.Auth is { } auth && JsonTemplateRenderer.RenderString(auth.ValueRef, context) is { } value)
        {
            headers[auth.In == DescriptorAuthLocation.Header ? auth.Name : $"?{auth.Name}"] =
                string.IsNullOrWhiteSpace(auth.Scheme) ? value : $"{auth.Scheme} {value}";
        }

        foreach ((string name, string template) in descriptor.Transport.Headers ?? [])
        {
            if (JsonTemplateRenderer.RenderString(template, context) is { } headerValue)
            {
                headers[name] = headerValue;
            }
        }

        string? body = null;

        if (descriptor.Submit.Body is { Kind: DescriptorBodyKind.Json, Template: { } template2 })
        {
            JsonTemplateRenderer.TryRender(template2, context, out JsonNode? rendered);
            body = rendered?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        if ((descriptor.Poll?.Mode ?? DescriptorPollMode.None) != DescriptorPollMode.None && descriptor.Poll!.Mode != DescriptorPollMode.PathTemplate)
        {
            warnings.Add("poll lấy URL từ phản hồi submit — host của URL đó cũng phải nằm trong allowlist, và nó chỉ lộ ra khi gọi thật.");
        }

        return new DescriptorPreviewResult(descriptor.Submit.Method, baseUrl + path, headers, body, warnings);
    }

    private static Dictionary<string, JsonNode?> VideoVariables(ProviderDescriptor descriptor)
    {
        VideoProviderCapability capability = descriptor.Capability!.Deserialize<VideoProviderCapability>(ProviderDescriptorParser.JsonOptions)!;

        var request = new VideoRequest
        {
            JobId = Guid.Empty,
            ShotIndex = 0,
            Prompt = "Ly cà phê sữa đá trên bàn gỗ, ánh nắng sớm, máy quay đẩy chậm vào.",
            NegativePrompt = "chữ, logo, watermark",
            DurationSeconds = capability.AllowedDurationSeconds.Min(),
            AspectRatio = capability.SupportedAspectRatios.Count > 0 ? capability.SupportedAspectRatios[0] : AspectRatio.Portrait9x16,
            ReferenceImageUrls = [SampleImageUrl],
            Seed = 42,
            SuppressNativeAudio = true,
        };

        return DeclarativeVideoProvider.BuildVariables(
            descriptor,
            capability.ModelId,
            capability,
            request,
            DeclarativeVideoProvider.ReferenceImages(descriptor, capability, request));
    }

    private static Dictionary<string, JsonNode?> TtsVariables(ProviderDescriptor descriptor)
    {
        TtsProviderCapability capability = descriptor.Capability!.Deserialize<TtsProviderCapability>(ProviderDescriptorParser.JsonOptions)!;

        return DeclarativeTtsProvider.BuildVariables(
            descriptor,
            capability.ModelId,
            capability,
            new TtsRequest
            {
                JobId = Guid.Empty,
                Text = "Cà phê CHU — đậm vị, mở cửa từ sáu giờ sáng.",
                VoiceId = "voice-id-mau",
                PreviousText = "Câu thoại của shot trước.",
                PreviousRequestId = "req-truoc",
            });
    }
}
