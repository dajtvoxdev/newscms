using System.Net;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers.ElevenLabs;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers;

public sealed class ElevenLabsVoicesTests
{
    private const string Key = "sk_bi-mat-cua-tai-khoan-0000";

    private static ElevenLabsVoices Create(ScriptedHttpHandler handler) =>
        new(new HttpClient(handler),
            new ResolvedCredential(ProviderNames.ElevenLabs, "eleven_flash_v2_5", ProviderCategory.TextToSpeech,
                "https://api.elevenlabs.io", Key, CredentialScope.System, null, 0),
            NullLogger.Instance);

    [Fact]
    public async Task Clone_gui_multipart_toi_voices_add_kem_key_va_doc_voice_id()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("""{"voice_id":"abc123","requires_verification":false}"""));

        VoiceCloneResult result = await Create(handler).CloneAsync(
            "Giọng chủ quán", "giọng nữ Hà Nội", [new VoiceSample("mau.wav", "audio/wav", [1, 2, 3])]);

        result.IsSuccess.Should().BeTrue();
        result.VoiceId.Should().Be("abc123");

        ScriptedHttpHandler.RecordedRequest request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.ToString().Should().Be("https://api.elevenlabs.io/v1/voices/add");
        request.Headers["xi-api-key"].Should().Be(Key);
        request.Body.Should().Contain("name=files").And.Contain("Giọng chủ quán").And.Contain("remove_background_noise");
    }

    [Fact]
    public async Task Clone_bi_tu_choi_thi_tra_ly_do_doc_duoc_va_khong_lo_key()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json(
            """{"detail":{"status":"voice_limit_reached","message":"You have reached your voice limit (KEY)"}}""".Replace("KEY", Key, StringComparison.Ordinal),
            HttpStatusCode.BadRequest));

        VoiceCloneResult result = await Create(handler).CloneAsync("x", null, [new VoiceSample("a.mp3", "audio/mpeg", [1])]);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("voice limit").And.NotContain(Key);
        result.RawError.Should().NotContain(Key);
    }

    [Fact]
    public async Task Liet_ke_thu_vien_doc_du_nhan_va_link_nghe_thu()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("""
            {"voices":[{"voice_id":"v1","name":"Rachel","category":"premade","preview_url":"https://cdn/v1.mp3","labels":{"gender":"female","accent":"american"}},
                       {"name":"thiếu id"}]}
            """));

        IReadOnlyList<ProviderVoice> voices = await Create(handler).ListAsync();

        ProviderVoice v = voices.Should().ContainSingle().Which;
        v.VoiceId.Should().Be("v1");
        v.PreviewUrl.Should().Be("https://cdn/v1.mp3");
        v.Labels["gender"].Should().Be("female");
    }

    [Fact]
    public async Task Xoa_goi_DELETE_dung_giong()
    {
        var handler = new ScriptedHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        (await Create(handler).DeleteAsync("abc 123")).Should().BeTrue();

        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().Uri.AbsolutePath.Should().Be("/v1/voices/abc%20123");
    }
}
