using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers;
using FluentAssertions;

namespace AdVideo.Tests.Providers;

/// <summary>P0.1 + P0.2 — manifest khởi đầu không được hứa năng lực model không có, và không được khai giá sai nhiều lần.</summary>
public class ProviderCapabilityCatalogTests
{
    [Fact]
    public void ElevenLabs_dung_model_doc_duoc_tieng_Viet()
    {
        TtsProviderCapability caps = ProviderCapabilityCatalog.Tts(ProviderNames.ElevenLabs)!;

        // eleven_multilingual_v2 không có tiếng Việt; manifest khai "vi" với model đó là hứa suông.
        caps.ModelId.Should().Be("eleven_flash_v2_5");
        caps.SupportedLanguages.Should().Contain("vi");
        caps.CostPer1000CharsUsd.Should().Be(0.05m);
    }

    [Theory]
    [InlineData(ProviderNames.Kling, 0.11)]
    [InlineData(ProviderNames.Seedance, 0.10)]
    public void Gia_video_khop_bang_gia_doc_lai_25_09_2026(string provider, double expected)
    {
        ProviderCapabilityCatalog.Video(provider)!.CostPerSecondUsd.Should().Be((decimal)expected);
    }
}
