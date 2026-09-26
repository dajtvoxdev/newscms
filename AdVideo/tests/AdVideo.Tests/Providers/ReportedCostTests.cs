using System.Net;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers;
using AdVideo.Infrastructure.Providers.ElevenLabs;
using AdVideo.Infrastructure.Providers.Fal;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers;

/// <summary>
/// P0.5 — <c>ReportedCostUsd</c> chỉ có khi provider THẬT SỰ báo giá.
/// </summary>
/// <remarks>
/// Adapter điền đơn giá manifest vào đây là làm <c>CostIsReported = true</c> cho mọi lời gọi, và
/// mất hẳn khả năng đối soát hoá đơn.
/// </remarks>
public class ReportedCostTests
{
    [Fact]
    public async Task Fal_khong_bao_gia_thi_adapter_khong_tu_dien()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/fal-ai/kling" => ScriptedHttpHandler.Json(
                "{\"request_id\":\"r1\",\"status_url\":\"https://queue.fal.run/s/r1\",\"response_url\":\"https://queue.fal.run/r/r1\"}"),
            "/s/r1" => ScriptedHttpHandler.Json("{\"status\":\"COMPLETED\"}"),
            _ => ScriptedHttpHandler.Json("{\"video\":{\"url\":\"https://cdn.fal.media/v.mp4\"}}"),
        });

        var provider = new FalQueueVideoProvider(
            new HttpClient(handler),
            new ResolvedCredential(ProviderNames.Kling, "fal-ai/kling", ProviderCategory.Video,
                "https://queue.fal.run", "k", CredentialScope.System, null, 0),
            ProviderCapabilityCatalog.Video(ProviderNames.Kling)!,
            NullLogger.Instance);

        VideoResult result = await provider.GenerateAsync(new VideoRequest
        {
            JobId = Guid.NewGuid(),
            ShotIndex = 0,
            Prompt = "p",
            DurationSeconds = 5,
            AspectRatio = AspectRatio.Portrait9x16,
        });

        result.IsSuccess.Should().BeTrue();
        result.ReportedCostUsd.Should().BeNull();
    }

    [Fact]
    public async Task ElevenLabs_bao_so_ky_tu_chu_khong_bao_tien()
    {
        const string body =
            "{\"audio_base64\":\"AAAA\",\"alignment\":{\"characters\":[\"a\",\" \",\"b\"]," +
            "\"character_start_times_seconds\":[0,0.1,0.2],\"character_end_times_seconds\":[0.1,0.2,0.3]}}";

        var handler = new ScriptedHttpHandler((_, _) =>
        {
            HttpResponseMessage response = ScriptedHttpHandler.Json(body);
            response.Headers.Add("xi-character-count", "3");

            return response;
        });

        var provider = new ElevenLabsTtsProvider(
            new HttpClient(handler),
            new ResolvedCredential(ProviderNames.ElevenLabs, "eleven_flash_v2_5", ProviderCategory.TextToSpeech,
                "https://api.elevenlabs.io", "k", CredentialScope.System, null, 0),
            ProviderCapabilityCatalog.Tts(ProviderNames.ElevenLabs)!,
            NullLogger.Instance);

        TtsResult result = await provider.SynthesizeAsync(new TtsRequest
        {
            JobId = Guid.NewGuid(),
            Text = "a b",
            VoiceId = "v",
        });

        result.IsSuccess.Should().BeTrue();
        result.BilledCharacterCount.Should().Be(3);
        result.ReportedCostUsd.Should().BeNull();
        handler.Requests.Single().Body.Should().Contain("\"model_id\":\"eleven_flash_v2_5\"");
    }
}
