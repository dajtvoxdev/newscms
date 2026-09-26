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

/// <summary>P2 — key chỉ đi tới đúng origin của endpoint, và không nằm trong RawError.</summary>
public class AdapterSecretHandlingTests
{
    private const string FalKey = "fal-key-id:0123456789abcdef0123456789abcdef";

    private static FalQueueVideoProvider Fal(ScriptedHttpHandler handler) => new(
        new HttpClient(handler),
        new ResolvedCredential(ProviderNames.Kling, "fal-ai/kling", ProviderCategory.Video,
            "https://queue.fal.run", FalKey, CredentialScope.System, null, 0),
        ProviderCapabilityCatalog.Video(ProviderNames.Kling)!,
        NullLogger.Instance);

    private static VideoRequest Request() => new()
    {
        JobId = Guid.NewGuid(),
        ShotIndex = 0,
        Prompt = "p",
        DurationSeconds = 5,
        AspectRatio = AspectRatio.Portrait9x16,
    };

    [Fact]
    public async Task Fal_khong_gui_key_toi_status_url_o_host_khac()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.Host switch
        {
            "queue.fal.run" => ScriptedHttpHandler.Json(
                "{\"request_id\":\"r\",\"status_url\":\"https://attacker.example/s\",\"response_url\":\"https://attacker.example/r\"}"),
            _ => ScriptedHttpHandler.Json("{\"status\":\"FAILED\"}"),
        });

        await Fal(handler).GenerateAsync(Request());

        handler.Requests[0].Headers["Authorization"].Should().Be($"Key {FalKey}");
        handler.Requests.Skip(1).Should().OnlyContain(r => !r.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Fal_che_key_bi_doi_lai_trong_than_loi()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
            ScriptedHttpHandler.Json($"{{\"detail\":\"bad auth header: Key {FalKey}\"}}", HttpStatusCode.Unauthorized));

        VideoResult result = await Fal(handler).GenerateAsync(Request());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        result.RawError.Should().NotContain(FalKey).And.Contain("****cdef");
    }

    [Fact]
    public async Task ElevenLabs_gan_key_theo_request_va_che_trong_loi()
    {
        const string key = "sk_elevenlabs_0123456789abcdef";

        var handler = new ScriptedHttpHandler((_, _) =>
            ScriptedHttpHandler.Json($"{{\"detail\":{{\"status\":\"invalid_api_key\",\"message\":\"{key}\"}}}}", HttpStatusCode.Unauthorized));

        var provider = new ElevenLabsTtsProvider(
            new HttpClient(handler),
            new ResolvedCredential(ProviderNames.ElevenLabs, "eleven_flash_v2_5", ProviderCategory.TextToSpeech,
                "https://api.elevenlabs.io", key, CredentialScope.System, null, 0),
            ProviderCapabilityCatalog.Tts(ProviderNames.ElevenLabs)!,
            NullLogger.Instance);

        TtsResult result = await provider.SynthesizeAsync(new TtsRequest { JobId = Guid.NewGuid(), Text = "a", VoiceId = "v" });

        handler.Requests.Single().Headers["xi-api-key"].Should().Be(key);
        result.RawError.Should().NotContain(key);
        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
    }

    [Fact]
    public void Cung_origin_so_ca_scheme_host_va_cong()
    {
        var baseUri = new Uri("https://queue.fal.run/fal-ai/x");

        ProviderHttp.IsSameOrigin(baseUri, new Uri("https://QUEUE.fal.run/other")).Should().BeTrue();
        ProviderHttp.IsSameOrigin(baseUri, new Uri("http://queue.fal.run/other")).Should().BeFalse();
        ProviderHttp.IsSameOrigin(baseUri, new Uri("https://queue.fal.run:8443/other")).Should().BeFalse();
        ProviderHttp.IsSameOrigin(baseUri, new Uri("https://fal.run/other")).Should().BeFalse();
    }
}
