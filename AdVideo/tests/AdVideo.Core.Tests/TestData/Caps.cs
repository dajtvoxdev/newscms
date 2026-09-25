using AdVideo.Core.Enums;
using AdVideo.Core.Providers;

namespace AdVideo.Core.Tests.TestData;

/// <summary>
/// Dựng capability cho test.
/// </summary>
/// <remarks>
/// Có builder riêng vì <see cref="VideoProviderCapability"/> có rất nhiều thuộc tính
/// <c>required</c>: viết tay ở mỗi test thì một thuộc tính mới sẽ làm hỏng hàng chục file,
/// và quan trọng hơn là người đọc test không phân biệt được thuộc tính nào là NỘI DUNG của
/// test với thuộc tính nào chỉ để trình biên dịch im lặng.
/// </remarks>
internal static class Caps
{
    /// <summary>Lưới 4/6/8 — giống Veo. Dùng làm lưới mặc định trong hầu hết test timeline.</summary>
    internal static readonly IReadOnlyList<int> Grid468 = [4, 6, 8];

    /// <summary>Lưới 5/10 — giống Kling. Dùng khi cần một lưới thưa để thấy hiệu ứng làm tròn lên.</summary>
    internal static readonly IReadOnlyList<int> Grid510 = [5, 10];

    internal static VideoProviderCapability Video(
        string provider = ProviderNames.Kling,
        IReadOnlyList<int>? grid = null,
        IReadOnlyList<AspectRatio>? ratios = null,
        IReadOnlyList<VideoTier>? tiers = null,
        bool acceptsHumanFaces = true,
        bool generatesNativeAudio = false,
        bool nativeAudioOn = false,
        bool canSeparateSfx = false,
        bool supportsFrameChaining = false,
        int maxSubjects = 0,
        decimal costPerSecond = 0.05m) =>
        new()
        {
            Provider = provider,
            ModelId = $"{provider}/test-model",
            AllowedDurationSeconds = grid ?? Grid468,
            SupportedAspectRatios = ratios ?? [AspectRatio.Portrait9x16, AspectRatio.Landscape16x9],
            AcceptsHumanFaces = acceptsHumanFaces,
            GeneratesNativeAudio = generatesNativeAudio,
            NativeAudioEnabledByDefault = nativeAudioOn,
            CanSeparateSfxFromSpeech = canSeparateSfx,
            SupportsImageToVideo = true,
            SupportsFrameChaining = supportsFrameChaining,
            MaxSubjectsReliably = maxSubjects,
            ServesTiers = tiers ?? [VideoTier.Draft, VideoTier.Standard],
            CostPerSecondUsd = costPerSecond,
        };

    internal static TtsProviderCapability Tts(
        string provider = ProviderNames.ElevenLabs,
        bool hasWordTimings = true,
        decimal costPer1000Chars = 0.3m) =>
        new()
        {
            Provider = provider,
            ModelId = $"{provider}/test-voice",
            HasWordTimings = hasWordTimings,
            SupportedLanguages = ["vi"],
            CostPer1000CharsUsd = costPer1000Chars,
        };

    internal static VideoRequirements Requirements(
        int durationSeconds = 15,
        AspectRatio ratio = AspectRatio.Portrait9x16,
        VideoTier tier = VideoTier.Standard,
        bool hasPerson = false,
        bool needsFrameChaining = false,
        int subjectCount = 1) =>
        new()
        {
            TargetDurationSeconds = durationSeconds,
            AspectRatio = ratio,
            Tier = tier,
            HasPerson = hasPerson,
            NeedsFrameChaining = needsFrameChaining,
            SubjectCount = subjectCount,
        };

    /// <summary>Chuỗi từ liền nhau, mỗi từ 1 giây, không có khoảng lặng nào.</summary>
    internal static List<WordTiming> ContiguousWords(int count, double start = 0, double wordSeconds = 1)
    {
        var words = new List<WordTiming>(count);
        for (int i = 0; i < count; i++)
        {
            double from = start + (i * wordSeconds);
            words.Add(new WordTiming($"tu{i + 1}", from, from + wordSeconds));
        }
        return words;
    }
}
