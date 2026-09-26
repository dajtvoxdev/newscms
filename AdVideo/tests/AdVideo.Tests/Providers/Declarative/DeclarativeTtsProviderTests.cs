using System.Net;
using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Qc;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Providers.Declarative;
using AdVideo.Infrastructure.Providers.ElevenLabs;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers.Declarative;

public class DeclarativeTtsProviderTests
{
    private const string Key = "sk_elevenlabs_test_0123456789";

    private const string SuccessBody =
        "{\"audio_base64\":\"AAEC\",\"alignment\":{\"characters\":[\"X\",\"i\",\"n\",\" \",\"c\",\"h\",\"à\",\"o\",\",\"]," +
        "\"character_start_times_seconds\":[0,0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8]," +
        "\"character_end_times_seconds\":[0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8,0.9]}}";

    private static DeclarativeTtsProvider Create(ScriptedHttpHandler handler, Action<JsonObject>? mutate = null, IMediaInspector? inspector = null)
    {
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.ElevenLabs, mutate);

        return new DeclarativeTtsProvider(
            new HttpClient(handler),
            DescriptorTestKit.Credential(descriptor, Key),
            DescriptorTestKit.Capability<TtsProviderCapability>(descriptor),
            descriptor,
            new RecordingCredentialStore(),
            inspector ?? new FixedDurationInspector(0),
            NullLogger.Instance);
    }

    private static TtsRequest Request(string? previousId = null) => new()
    {
        JobId = Guid.NewGuid(),
        Text = "Xin chào,",
        VoiceId = "voice 1",
        Speed = 1.1m,
        PreviousRequestId = previousId,
        PreviousText = previousId is null ? null : "câu trước",
    };

    private static HttpResponseMessage Success()
    {
        HttpResponseMessage response = ScriptedHttpHandler.Json(SuccessBody);
        response.Headers.Add("xi-character-count", "9");
        response.Headers.Add("request-id", "req-42");

        return response;
    }

    [Fact]
    public async Task Doc_audio_moc_ky_tu_va_ghep_tu_bang_cung_ham_voi_adapter()
    {
        var handler = new ScriptedHttpHandler((_, _) => Success());

        TtsResult result = await Create(handler).SynthesizeAsync(Request());

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.CanLockTimeline.Should().BeTrue();
        result.AudioBytes.Should().Equal(0, 1, 2);
        result.CharacterTimings.Should().HaveCount(9);
        result.WordTimings.Select(w => w.Word).Should().Equal("Xin", "chào,");
        result.AudioDurationSeconds.Should().Be(0.9);
        result.BilledCharacterCount.Should().Be(9);
        result.ProviderRequestId.Should().Be("req-42");
        result.EstimatedCostUsd.Should().Be(0.05m * 9 / 1000m);
        result.ReportedCostUsd.Should().BeNull();

        ScriptedHttpHandler.RecordedRequest sent = handler.Requests.Single();
        sent.Uri.AbsoluteUri.Should().Be("https://api.elevenlabs.io/v1/text-to-speech/voice%201/with-timestamps?output_format=mp3_44100_128");
        sent.Headers["xi-api-key"].Should().Be(Key);
    }

    /// <summary>P4.2 — body ElevenLabs bằng descriptor phải giống từng byte adapter viết tay.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("req-truoc")]
    public async Task Golden_descriptor_ElevenLabs_dung_body_giong_het_adapter_cu(string? previousId)
    {
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.ElevenLabs);
        TtsProviderCapability capability = DescriptorTestKit.Capability<TtsProviderCapability>(descriptor);
        ResolvedCredential credential = DescriptorTestKit.Credential(descriptor, Key);

        var legacy = new ScriptedHttpHandler((_, _) => Success());
        TtsResult old = await new ElevenLabsTtsProvider(new HttpClient(legacy), credential, capability, NullLogger.Instance).SynthesizeAsync(Request(previousId));

        var declarative = new ScriptedHttpHandler((_, _) => Success());
        TtsResult neu = await Create(declarative).SynthesizeAsync(Request(previousId));

        declarative.Requests[0].Uri.Should().Be(legacy.Requests[0].Uri);
        declarative.Requests[0].Body.Should().Be(legacy.Requests[0].Body);
        neu.WordTimings.Should().Equal(old.WordTimings);
        neu.CharacterTimings.Should().Equal(old.CharacterTimings);
        neu.AudioDurationSeconds.Should().Be(old.AudioDurationSeconds);
    }

    [Fact]
    public async Task Khai_co_moc_ma_khong_doc_duoc_moc_thi_fail_ngay()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("{\"audio_base64\":\"AAEC\"}"));

        TtsResult result = await Create(handler).SynthesizeAsync(Request());

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("mốc thời gian");
    }

    [Fact]
    public async Task Moc_theo_tu_tinh_bang_mili_giay()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json(
            "{\"audio_base64\":\"AAEC\",\"words\":[\"Xin\",\"chào\"],\"s\":[0,400],\"e\":[350,900,1200]}"));

        TtsResult result = await Create(handler, d =>
        {
            d["capability"]!["hasCharacterTimings"] = false;
            d["result"]!["alignment"] = new JsonObject
            {
                ["format"] = "words",
                ["textPath"] = "words",
                ["startsPath"] = "s",
                ["endsPath"] = "e",
                ["timeUnit"] = "milliseconds",
            };
        }).SynthesizeAsync(Request());

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.CharacterTimings.Should().BeEmpty();
        result.WordTimings.Should().Equal(new WordTiming("Xin", 0, 0.35), new WordTiming("chào", 0.4, 0.9));
        result.AudioDurationSeconds.Should().Be(0.9);
    }

    [Fact]
    public async Task Khong_co_alignment_thi_do_do_dai_bang_ffprobe()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.Host == "api.elevenlabs.io"
            ? ScriptedHttpHandler.Json("{\"audio_url\":\"https://cdn.example/a.mp3\"}")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 9]) });

        TtsResult result = await Create(handler, d =>
        {
            d["capability"]!["hasWordTimings"] = false;
            d["capability"]!["hasCharacterTimings"] = false;
            d["result"]!.AsObject().Remove("alignment");
            d["result"]!.AsObject().Remove("audioBase64Path");
            d["result"]!["audioUrlPath"] = "audio_url";
        }, new FixedDurationInspector(3.5)).SynthesizeAsync(Request());

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.AudioBytes.Should().Equal(9, 9);
        result.AudioDurationSeconds.Should().Be(3.5);
        result.CanLockTimeline.Should().BeFalse();
        handler.Requests[1].Headers.Should().NotContainKey("xi-api-key", "link audio ở host khác không được mang key");
    }

    [Fact]
    public async Task Loi_quota_trong_than_tat_credential()
    {
        var credentials = new RecordingCredentialStore();
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.ElevenLabs);

        var handler = new ScriptedHttpHandler((_, _) =>
            ScriptedHttpHandler.Json("{\"detail\":{\"status\":\"quota_exceeded\"}}", HttpStatusCode.PaymentRequired));

        TtsResult result = await new DeclarativeTtsProvider(
                new HttpClient(handler),
                DescriptorTestKit.Credential(descriptor, Key),
                DescriptorTestKit.Capability<TtsProviderCapability>(descriptor),
                descriptor,
                credentials,
                new FixedDurationInspector(0),
                NullLogger.Instance)
            .SynthesizeAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        credentials.Deactivated.Should().ContainSingle();
    }

    [Fact]
    public async Task Van_ban_qua_dai_thi_tu_choi_truoc_khi_goi()
    {
        var handler = new ScriptedHttpHandler((_, _) => Success());

        TtsResult result = await Create(handler).SynthesizeAsync(new TtsRequest { JobId = Guid.NewGuid(), Text = new string('a', 5001), VoiceId = "v" });

        result.FailureKind.Should().Be(VideoFailureKind.ContentRejected);
        handler.Requests.Should().BeEmpty();
    }

    private sealed class FixedDurationInspector(double seconds) : IMediaInspector
    {
        public Task<MediaProbeResult> ProbeAsync(string filePath, bool measureLoudness = false, bool detectBlackFrames = false, CancellationToken cancellationToken = default)
        {
            File.Exists(filePath).Should().BeTrue("audio phải được ghi ra file tạm trước khi đo");

            return Task.FromResult(new MediaProbeResult(seconds, 0, 0, null, true, "mp3", 2));
        }
    }
}
