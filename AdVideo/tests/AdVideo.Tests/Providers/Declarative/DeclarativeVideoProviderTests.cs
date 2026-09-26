using System.Net;
using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers.Declarative;
using AdVideo.Infrastructure.Providers.Fal;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers.Declarative;

public class DeclarativeVideoProviderTests
{
    private const string NovaKey = "nova-key-0123456789abcdef";

    private static (DeclarativeVideoProvider Provider, RecordingCredentialStore Credentials, DescriptorTestKit.CountingDelay Delay) Nova(
        ScriptedHttpHandler handler,
        Action<JsonObject>? mutate = null)
    {
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.Nova, mutate);
        var credentials = new RecordingCredentialStore();
        var delay = new DescriptorTestKit.CountingDelay();

        var provider = new DeclarativeVideoProvider(
            new HttpClient(handler),
            DescriptorTestKit.Credential(descriptor, NovaKey),
            DescriptorTestKit.Capability<VideoProviderCapability>(descriptor),
            descriptor,
            credentials,
            NullLogger.Instance,
            delay.DelayAsync);

        return (provider, credentials, delay);
    }

    private static VideoRequest Request(string prompt = "Ly cà phê \"đặc biệt\" }", params string[] images) => new()
    {
        JobId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ShotIndex = 2,
        Prompt = prompt,
        DurationSeconds = 6,
        AspectRatio = AspectRatio.Portrait9x16,
        ReferenceImageUrls = images,
        SuppressNativeAudio = true,
    };

    [Fact]
    public async Task NOVA_submit_poll_roi_doc_url_video()
    {
        int polls = 0;
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/videos" => ScriptedHttpHandler.Json("{\"id\":\"vid_1\",\"status\":\"queued\",\"error\":0}"),
            "/v1/videos/vid_1" => ++polls < 3
                ? ScriptedHttpHandler.Json("{\"status\":\"processing\"}")
                : ScriptedHttpHandler.Json("{\"status\":\"completed\",\"url\":\"https://cdn.novagateway.net/v/1.mp4\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        (DeclarativeVideoProvider provider, _, DescriptorTestKit.CountingDelay delay) = Nova(handler);

        VideoResult result = await provider.GenerateAsync(Request(images: ["https://img/1.jpg", "https://img/2.jpg"]));

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.VideoUri.Should().Be(new Uri("https://cdn.novagateway.net/v/1.mp4"));
        result.ProviderRequestId.Should().Be("vid_1");
        result.ReportedCostUsd.Should().BeNull();
        result.EstimatedCostUsd.Should().Be(0.08m * 6 + 0.01m);
        result.DescriptorSha256.Should().HaveLength(64);
        result.HasNativeAudio.Should().BeFalse();
        delay.Calls.Should().Be(2);

        ScriptedHttpHandler.RecordedRequest submit = handler.Requests[0];
        submit.Method.Should().Be(HttpMethod.Post);
        submit.Headers["Authorization"].Should().Be($"Bearer {NovaKey}");
        submit.Headers["Accept"].Should().Be("application/json");

        JsonNode body = JsonNode.Parse(submit.Body!)!;
        body["model"]!.GetValue<string>().Should().Be("xai/grok-imagine-video-1.5");
        body["prompt"]!.GetValue<string>().Should().Be("Ly cà phê \"đặc biệt\" }");
        body["seconds"]!.GetValue<string>().Should().Be("6");
        body["extra_body"]!["resolution"]!.GetValue<string>().Should().Be("480p");
        body["extra_body"]!["aspect_ratio"]!.GetValue<string>().Should().Be("9:16");
        body["image"]!["url"]!.GetValue<string>().Should().Be("https://img/1.jpg");

        handler.Requests.Skip(1).Should().OnlyContain(r => r.Uri.AbsolutePath == "/v1/videos/vid_1" && r.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Khong_co_anh_thi_nhanh_when_bien_mat()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"completed\",\"video_url\":\"https://cdn/x.mp4\"}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request());

        result.IsSuccess.Should().BeTrue();
        JsonNode.Parse(handler.Requests[0].Body!)!.AsObject().ContainsKey("image").Should().BeFalse();
    }

    [Fact]
    public async Task HTTP_200_ma_than_bao_loi_thi_doc_ma_loi()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
            ScriptedHttpHandler.Json("{\"error\":{\"code\":\"content_policy\",\"message\":\"bị chặn\"}}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(VideoFailureKind.ContentRejected);
        result.CanRetry.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Het_tien_402_thi_tat_credential_va_khong_retry()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
            ScriptedHttpHandler.Json($"{{\"message\":\"insufficient balance for {NovaKey}\"}}", HttpStatusCode.PaymentRequired));

        (DeclarativeVideoProvider provider, RecordingCredentialStore credentials, _) = Nova(handler);

        VideoResult result = await provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        result.RawError.Should().NotContain(NovaKey);
        credentials.Deactivated.Should().ContainSingle().Which.Provider.Should().Be("nova-grok-video-15");
    }

    [Fact]
    public async Task Rate_limit_mang_theo_retry_after()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
        {
            HttpResponseMessage response = ScriptedHttpHandler.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

            return response;
        });

        (DeclarativeVideoProvider provider, RecordingCredentialStore credentials, _) = Nova(handler);

        VideoResult result = await provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.RateLimited);
        result.RetryAfterSeconds.Should().Be(7);
        credentials.Deactivated.Should().BeEmpty();
    }

    [Fact]
    public async Task Poll_bao_that_bai_thi_phan_loai_theo_than()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"failed\",\"error\":{\"code\":\"content_policy\"}}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.ContentRejected);
        result.ProviderRequestId.Should().Be("v");
    }

    [Fact]
    public async Task Het_tran_poll_thi_dung_va_khong_cho_retry()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"trang-thai-moi-la\"}"));

        (DeclarativeVideoProvider provider, _, DescriptorTestKit.CountingDelay delay) = Nova(handler, d =>
        {
            d["poll"]!["intervalSeconds"] = 10;
            d["poll"]!["maxWaitSeconds"] = 30;
        });

        VideoResult result = await provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        result.FailureReason.Should().Contain("30 giây");
        handler.Requests.Should().HaveCount(1 + 3);
        delay.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Ti_le_khung_khong_co_trong_value_map_thi_khong_goi_provider()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("{}"));

        (DeclarativeVideoProvider provider, _, _) = Nova(handler, d => d["valueMaps"]!["aspect"]!.AsObject().Remove("Portrait9x16"));

        VideoResult result = await provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        result.FailureReason.Should().Contain("valueMaps.aspect");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Prompt_qua_dai_thi_tu_choi_truoc_khi_goi()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("{}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request(new string('a', 5001)));

        result.FailureKind.Should().Be(VideoFailureKind.ContentRejected);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Loi_mang_la_loi_tam_thoi()
    {
        var handler = new ScriptedHttpHandler((_, _) => throw new HttpRequestException($"reset {NovaKey}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request());

        result.FailureKind.Should().Be(VideoFailureKind.Transient);
        result.RawError.Should().NotContain(NovaKey);
    }

    [Fact]
    public async Task Bao_xong_ma_khong_co_video_thi_la_loi()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"completed\"}"));

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request());

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("không tìm thấy video");
    }

    [Fact]
    public async Task Tai_file_theo_content_path_kem_key_cung_origin()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/videos" => ScriptedHttpHandler.Json("{\"id\":\"v9\",\"error\":0}"),
            "/v1/videos/v9/content" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) },
            _ => ScriptedHttpHandler.Json("{\"status\":\"completed\"}"),
        });

        VideoResult result = await Nova(handler, d =>
        {
            d["result"]!.AsObject().Remove("videoUrlPath");
            d["result"]!["contentPath"] = "/videos/{{provider_request_id}}/content";
        }).Provider.GenerateAsync(Request());

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.VideoBytes.Should().Equal(1, 2, 3);
        result.VideoUri.Should().BeNull();
        handler.Requests[^1].Headers.Should().ContainKey("Authorization");
    }

    [Fact]
    public async Task Provider_tu_bao_gia_thi_la_gia_bao_ve()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"completed\",\"url\":\"https://cdn/x.mp4\",\"usage\":{\"cost\":\"0.33\"}}"));

        VideoResult result = await Nova(handler, d => d["result"]!["reportedCostPath"] = "usage.cost").Provider.GenerateAsync(Request());

        result.ReportedCostUsd.Should().Be(0.33m);
    }

    /// <summary>
    /// P4.1 — bằng chứng engine đủ sức: body do descriptor fal dựng phải BẰNG TỪNG BYTE body của
    /// adapter FalQueueVideoProvider, trước khi bỏ adapter.
    /// </summary>
    [Theory]
    [MemberData(nameof(GoldenRequests))]
    public async Task Golden_descriptor_fal_dung_body_giong_het_adapter_cu(VideoRequest request)
    {
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.FalKling);
        VideoProviderCapability capability = DescriptorTestKit.Capability<VideoProviderCapability>(descriptor);
        ResolvedCredential credential = DescriptorTestKit.Credential(descriptor, "fal-key");

        static ScriptedHttpHandler Handler() => new((_, _) => ScriptedHttpHandler.Json("{\"detail\":\"stop\"}", HttpStatusCode.BadRequest));

        ScriptedHttpHandler legacy = Handler();
        await new FalQueueVideoProvider(new HttpClient(legacy), credential, capability, NullLogger.Instance).GenerateAsync(request);

        ScriptedHttpHandler declarative = Handler();
        await new DeclarativeVideoProvider(new HttpClient(declarative), credential, capability, descriptor, new RecordingCredentialStore(), NullLogger.Instance)
            .GenerateAsync(request);

        declarative.Requests[0].Uri.Should().Be(legacy.Requests[0].Uri);
        declarative.Requests[0].Headers["Authorization"].Should().Be(legacy.Requests[0].Headers["Authorization"]);
        declarative.Requests[0].Body.Should().Be(legacy.Requests[0].Body);
    }

    public static TheoryData<VideoRequest> GoldenRequests => new()
    {
        new VideoRequest
        {
            JobId = Guid.NewGuid(), ShotIndex = 0, Prompt = "Bánh mì \"giòn\" <nóng> & thơm", NegativePrompt = "chữ, logo",
            DurationSeconds = 5, AspectRatio = AspectRatio.Portrait9x16, Seed = 42, SuppressNativeAudio = true,
            ReferenceImageUrls = ["https://img/1.jpg", "https://img/2.jpg", "https://img/3.jpg"],
        },
        new VideoRequest
        {
            JobId = Guid.NewGuid(), ShotIndex = 1, Prompt = "p", DurationSeconds = 10,
            AspectRatio = AspectRatio.Landscape16x9, SuppressNativeAudio = false,
        },
        new VideoRequest
        {
            JobId = Guid.NewGuid(), ShotIndex = 2, Prompt = "p", NegativePrompt = "  ", DurationSeconds = 5,
            AspectRatio = AspectRatio.Square1x1, Seed = 0, ReferenceImageUrls = ["https://img/only.jpg"],
        },
    };

    [Fact]
    public async Task Fal_theo_status_url_va_response_url_tu_phan_hoi()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/fal-ai/kling-video/v3/pro/image-to-video" => ScriptedHttpHandler.Json(
                "{\"request_id\":\"r1\",\"status_url\":\"https://queue.fal.run/req/r1/status\",\"response_url\":\"https://queue.fal.run/req/r1\"}"),
            "/req/r1/status" => ScriptedHttpHandler.Json("{\"status\":\"COMPLETED\"}"),
            _ => ScriptedHttpHandler.Json("{\"video\":{\"url\":\"https://v3.fal.media/files/a.mp4\"}}"),
        });

        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.FalKling);

        VideoResult result = await new DeclarativeVideoProvider(
                new HttpClient(handler),
                DescriptorTestKit.Credential(descriptor, "fal-key"),
                DescriptorTestKit.Capability<VideoProviderCapability>(descriptor),
                descriptor,
                new RecordingCredentialStore(),
                NullLogger.Instance)
            .GenerateAsync(Request());

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        result.VideoUri.Should().Be(new Uri("https://v3.fal.media/files/a.mp4"));
        result.EstimatedCostUsd.Should().Be(0.66m);
        handler.Requests.Select(r => r.Uri.AbsolutePath).Should().Equal(
            "/fal-ai/kling-video/v3/pro/image-to-video", "/req/r1/status", "/req/r1");
    }

    [Fact]
    public async Task Ping_khong_goi_mang()
    {
        var handler = new ScriptedHttpHandler((_, _) => throw new InvalidOperationException("không được gọi"));

        (await Nova(handler).Provider.PingAsync()).Should().BeTrue();
    }
}
