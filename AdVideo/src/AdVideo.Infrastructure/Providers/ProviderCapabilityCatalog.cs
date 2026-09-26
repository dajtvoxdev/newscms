using AdVideo.Core.Enums;
using AdVideo.Core.Providers;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Manifest khởi đầu cho từng provider, dùng khi nạp credential lần đầu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Đây KHÔNG phải nguồn sự thật lúc chạy.</b> Nguồn sự thật là cột <c>CapabilityJson</c> trong
/// bảng <c>ProviderCredentials</c> (D10) — provider đổi giới hạn hay đổi giá thì sửa một dòng DB,
/// không deploy. Catalog này chỉ trả lời câu hỏi "điền gì vào cột đó lần đầu", để người vận hành
/// không phải gõ tay một khối JSON 20 dòng.
/// </para>
/// <para>
/// <b>Mọi con số ở đây đều PHẢI kiểm lại trước khi tiêu tiền thật.</b> Giá lấy từ bảng so sánh
/// công khai 7–9/2026 trong tài liệu thiết kế, và giá của các dịch vụ này thay đổi theo tháng.
/// Điền thấp hơn thực tế nghĩa là dự toán thấp hơn hoá đơn — và một cái trần chi tiêu luôn thấp
/// hơn hoá đơn thì không phải là trần.
/// </para>
/// </remarks>
public static class ProviderCapabilityCatalog
{
    /// <summary>Manifest video mặc định theo tên provider. Null nếu chưa có mẫu.</summary>
    public static VideoProviderCapability? Video(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            ProviderNames.Kling => new VideoProviderCapability
            {
                Provider = ProviderNames.Kling,
                ModelId = "fal-ai/kling-video/v3/pro/image-to-video",
                AllowedDurationSeconds = [5, 10],
                SupportedAspectRatios =
                    [AspectRatio.Portrait9x16, AspectRatio.Square1x1, AspectRatio.Landscape16x9],
                AcceptsHumanFaces = true,
                GeneratesNativeAudio = true,
                NativeAudioEnabledByDefault = false,
                CanSeparateSfxFromSpeech = false,
                SupportsImageToVideo = true,
                SupportsFrameChaining = true,
                MaxReferenceImages = 2,
                MaxSubjectsReliably = 2,
                ServesTiers = [VideoTier.Draft, VideoTier.Standard],

                // Đọc lại 25/09/2026: ~0,11 USD/giây. Số cũ 0,17 cao gấp 1,5 lần — dự toán sai về
                // phía an toàn vẫn là sai: trần chi tiêu từ chối oan job hợp lệ.
                CostPerSecondUsd = 0.11m,
                SupportsSeed = true,
                SupportsLipSync = false,
            },

            ProviderNames.Seedance => new VideoProviderCapability
            {
                Provider = ProviderNames.Seedance,
                ModelId = "fal-ai/bytedance/seedance/v2/pro/image-to-video",
                AllowedDurationSeconds = [4, 6, 8, 10, 12, 15],
                SupportedAspectRatios =
                    [AspectRatio.Portrait9x16, AspectRatio.Square1x1, AspectRatio.Landscape16x9],
                AcceptsHumanFaces = true,
                GeneratesNativeAudio = true,
                NativeAudioEnabledByDefault = false,
                CanSeparateSfxFromSpeech = false,
                SupportsImageToVideo = true,
                SupportsFrameChaining = true,
                MaxReferenceImages = 4,
                MaxSubjectsReliably = 2,
                ServesTiers = [VideoTier.Premium],

                // Giá thật 0,034–0,10 USD/giây tuỳ độ phân giải; lấy mức TRÊN của khoảng. Số cũ
                // 0,30 cao gấp 3–9 lần. Xem ke-hoach-provider-khai-bao-2026-09-25.md mục 1.3.
                CostPerSecondUsd = 0.10m,
                SupportsSeed = true,
                SupportsLipSync = false,
            },

            ProviderNames.Vidu => new VideoProviderCapability
            {
                Provider = ProviderNames.Vidu,
                ModelId = "fal-ai/vidu/q2/reference-to-video",
                AllowedDurationSeconds = [4, 5, 8, 10],
                SupportedAspectRatios =
                    [AspectRatio.Portrait9x16, AspectRatio.Square1x1, AspectRatio.Landscape16x9],
                AcceptsHumanFaces = true,
                GeneratesNativeAudio = true,

                // BẬT SẴN. Đây là chỗ dễ quên nhất trong cả hệ thống: quên tắt là video có hai lớp
                // tiếng, và người xem chỉ thấy "tiếng kỳ kỳ" chứ không ai chỉ ra được vì sao.
                NativeAudioEnabledByDefault = true,

                // Vidu là provider duy nhất tách được thoại khỏi tiếng động — nghĩa là nó cũng là
                // provider duy nhất giữ được sfx ở −18 dB mà không kéo theo thoại của model (D3).
                CanSeparateSfxFromSpeech = true,
                SupportsImageToVideo = true,
                SupportsFrameChaining = false,
                MaxReferenceImages = 7,
                MaxSubjectsReliably = 3,
                ServesTiers = [VideoTier.Standard],
                CostPerSecondUsd = 0.085m,
                SupportsSeed = true,
                SupportsLipSync = false,
            },

            _ => null,
        };

    /// <summary>Manifest TTS mặc định theo tên provider. Null nếu chưa có mẫu.</summary>
    public static TtsProviderCapability? Tts(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            ProviderNames.ElevenLabs => new TtsProviderCapability
            {
                Provider = ProviderNames.ElevenLabs,
                // eleven_multilingual_v2 KHÔNG có tiếng Việt — tiếng Việt chỉ có từ Flash/Turbo v2.5.
                // Flash thay Turbo (ElevenLabs đã xếp Turbo vào mục bị thay thế) và rẻ bằng nửa v2.
                ModelId = "eleven_flash_v2_5",

                // Mốc theo từ là điều kiện để khoá timeline (bước 5). Không có nó thì bước 5 mù.
                HasWordTimings = true,
                HasCharacterTimings = true,
                SupportedLanguages = ["vi", "en"],
                SupportsVoiceCloning = true,
                MinCloneSampleSeconds = 60,
                HasVoiceOwnershipVerification = true,
                SupportsProsodyContinuation = true,
                // Giá Flash v2.5. Số cũ 0,30 cao gấp 3–6 lần kể cả so với model v2/v3.
                CostPer1000CharsUsd = 0.05m,
                RealTimeFactor = 0.3m,

                // Giọng "Default" của ElevenLabs ngừng phục vụ 31/12/2026. Ghi vào manifest để
                // registry loại provider TRƯỚC khi gọi, thay vì phát hiện qua một job hỏng.
                VoicesExpireAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            },

            ProviderNames.VieNeu => new TtsProviderCapability
            {
                Provider = ProviderNames.VieNeu,
                ModelId = "vieneu-tts",

                // false cho tới khi đo được: adapter VieNeu trả mảng mốc rỗng, nên tier thành phẩm
                // không dùng được engine này. Khai true ở đây là mở đường cho một video lệch tiếng.
                HasWordTimings = false,
                HasCharacterTimings = false,
                SupportedLanguages = ["vi"],
                SupportsVoiceCloning = true,
                MinCloneSampleSeconds = 10,
                HasVoiceOwnershipVerification = false,
                SupportsProsodyContinuation = false,

                // Tự host: không trả tiền theo ký tự. 0 ở đây nghĩa là "miễn phí", khác với
                // "chưa biết giá" — và khác đó ảnh hưởng thẳng tới dự toán.
                CostPer1000CharsUsd = 0m,
                RealTimeFactor = 0.5m,
                VoicesExpireAt = null,
            },

            _ => null,
        };

    /// <summary>Provider này thuộc loại nào, suy từ tên. Null nếu không nhận ra.</summary>
    public static ProviderCategory? CategoryOf(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            ProviderNames.Kling or ProviderNames.Seedance or ProviderNames.Vidu
                or ProviderNames.Runway => ProviderCategory.Video,
            ProviderNames.ElevenLabs or ProviderNames.VieNeu => ProviderCategory.TextToSpeech,
            _ => null,
        };

    /// <summary>Endpoint mặc định. Bỏ trống thì adapter tự dùng endpoint dựng sẵn của nó.</summary>
    public static string? DefaultEndpoint(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            ProviderNames.Kling or ProviderNames.Seedance or ProviderNames.Vidu => "https://queue.fal.run",
            ProviderNames.ElevenLabs => "https://api.elevenlabs.io",
            ProviderNames.VieNeu => "http://127.0.0.1:8080",
            _ => null,
        };
}
