namespace AdVideo.Core.Providers.Descriptors;

/// <summary>
/// Không gian tên biến của descriptor — <b>đóng và có kiểm</b>.
/// </summary>
/// <remarks>
/// Biến lạ thì <see cref="DescriptorValidator"/> từ chối lưu, không im lặng bỏ qua: một lỗi gõ
/// <c>{{promt}}</c> mà được lưu thì provider nhận body thiếu prompt, và lỗi lộ ra ở hoá đơn.
/// </remarks>
public static class DescriptorVariables
{
    public const string ModelId = "model_id";
    public const string JobId = "job_id";
    public const string ProviderRequestId = "provider_request_id";

    // Video
    public const string Prompt = "prompt";
    public const string NegativePrompt = "negative_prompt";
    public const string Seconds = "seconds";
    public const string AspectRatio = "aspect_ratio";
    public const string ImageUrl = "image_url";
    public const string ImageUrls = "image_urls";
    public const string Seed = "seed";
    public const string GenerateAudio = "generate_audio";
    public const string ShotIndex = "shot_index";

    // TTS
    public const string Text = "text";
    public const string VoiceId = "voice_id";
    public const string Speed = "speed";
    public const string PreviousText = "previous_text";
    public const string PreviousRequestId = "previous_request_id";

    /// <summary>Tiền tố secret. Chỉ hợp lệ trong <c>transport.auth</c> và <c>transport.headers</c>.</summary>
    public const string SecretPrefix = "secret.";

    /// <summary>Secret duy nhất hiện có: API key đã giải mã của credential.</summary>
    public const string SecretApiKey = "secret.api_key";

    public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.Ordinal)
    {
        ModelId, JobId, Prompt, NegativePrompt, Seconds, AspectRatio, ImageUrl, ImageUrls, Seed, GenerateAudio, ShotIndex,
    };

    public static readonly IReadOnlySet<string> Tts = new HashSet<string>(StringComparer.Ordinal)
    {
        ModelId, JobId, Text, VoiceId, Speed, PreviousText, PreviousRequestId,
    };

    /// <summary>Biến dựng sẵn cho một loại provider, CHƯA gồm <c>defaults</c> và <c>provider_request_id</c>.</summary>
    public static IReadOnlySet<string> For(DescriptorKind kind) => kind == DescriptorKind.Tts ? Tts : Video;

    /// <summary>Mọi tên biến dựng sẵn ở mọi loại — <c>defaults</c> không được đặt trùng những tên này.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(
        Video.Concat(Tts).Append(ProviderRequestId).Append(JsonTemplateRenderer.ItemVariable).Append(JsonTemplateRenderer.IndexVariable),
        StringComparer.Ordinal);
}
