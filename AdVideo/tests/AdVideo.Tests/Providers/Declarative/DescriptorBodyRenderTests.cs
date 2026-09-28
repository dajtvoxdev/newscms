using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers.Declarative;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers.Declarative;

/// <summary>
/// Thân request không dựng được thì KHÔNG được gửi đi. Gửi một thân khuyết khoá (hoặc rỗng) là gửi
/// một request khác với thứ descriptor mô tả — với prompt, đó là một lần render tính tiền cho nội
/// dung ngẫu nhiên. Bản xem trước cũng phải nói trước điều đó thay vì im lặng bỏ qua.
/// </summary>
public class DescriptorBodyRenderTests
{
    private const string NovaKey = "nova-key-0123456789abcdef";

    private static (DeclarativeVideoProvider Provider, RecordingCredentialStore Credentials) Nova(
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

        return (provider, credentials);
    }

    /// <summary>
    /// Descriptor image-to-video: gốc template thành chính node <c>@when</c> của ảnh, nên CẢ thân
    /// request biến mất khi không có ảnh tham chiếu nào — đúng ca "thiếu biến dựng body".
    /// </summary>
    private static void ThanBodyChiDungKhiCoAnh(JsonObject root) =>
        root["submit"]!["body"]!["template"] = root["submit"]!["body"]!["template"]!["image"]!.DeepClone();

    private static VideoRequest Request(params string[] images) => new()
    {
        JobId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ShotIndex = 0,
        Prompt = "Ly cà phê sữa đá trên bàn gỗ, ánh nắng sớm, máy quay đẩy chậm vào.",
        DurationSeconds = 6,
        AspectRatio = AspectRatio.Portrait9x16,
        ReferenceImageUrls = images,
        SuppressNativeAudio = true,
    };

    [Fact]
    public async Task Thieu_bien_dung_body_thi_khong_gui_request_nao()
    {
        var handler = new ScriptedHttpHandler((_, _) => ScriptedHttpHandler.Json("{}"));

        VideoResult result = await Nova(handler, ThanBodyChiDungKhiCoAnh).Provider.GenerateAsync(Request());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(VideoFailureKind.ProviderUnavailable);
        result.FailureReason.Should().Contain("thiếu biến để dựng submit.body");
        handler.Requests.Should().BeEmpty("request chưa gửi thì chưa có lần render nào bị tính tiền");
    }

    [Fact]
    public async Task Du_bien_dung_body_thi_van_gui_binh_thuong()
    {
        var handler = new ScriptedHttpHandler((request, _) => request.Method == HttpMethod.Post
            ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
            : ScriptedHttpHandler.Json("{\"status\":\"completed\",\"url\":\"https://cdn.novagateway.net/v/1.mp4\"}"));

        VideoResult result = await Nova(handler, ThanBodyChiDungKhiCoAnh)
            .Provider.GenerateAsync(Request("https://img/1.jpg"));

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        handler.Requests.Should().HaveCount(2);
        JsonNode.Parse(handler.Requests[0].Body!)!["url"]!.GetValue<string>().Should().Be("https://img/1.jpg");
    }

    /// <summary>
    /// Thân request (và cả request) phải được giải phóng sau khi gửi xong thay vì bỏ cho GC dọn.
    /// Đọc lại thân SAU khi provider chạy xong là bằng chứng nó đã bị giải phóng — còn việc handler
    /// đọc được thân lúc đang gửi là bằng chứng nó chưa bị giải phóng quá sớm.
    /// </summary>
    [Fact]
    public async Task Than_request_duoc_giai_phong_sau_khi_gui_xong()
    {
        HttpContent? thanRequest = null;

        var handler = new ScriptedHttpHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                thanRequest = request.Content;
            }

            return request.Method == HttpMethod.Post
                ? ScriptedHttpHandler.Json("{\"id\":\"v\",\"error\":0}")
                : ScriptedHttpHandler.Json("{\"status\":\"completed\",\"url\":\"https://cdn.novagateway.net/v/1.mp4\"}");
        });

        VideoResult result = await Nova(handler).Provider.GenerateAsync(Request("https://img/1.jpg"));

        result.IsSuccess.Should().BeTrue(result.FailureReason);
        thanRequest.Should().NotBeNull();
        handler.Requests[0].Body.Should().NotBeNullOrEmpty("lúc gửi thì thân request còn đọc được");

        await FluentActions.Awaiting(() => thanRequest!.ReadAsStringAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Chay_kho_canh_bao_khi_body_khong_dung_duoc()
    {
        // Descriptor cấu hình sai: khai không nhận ảnh tham chiếu (supportsImageToVideo = false) mà
        // thân request lại dựng từ ảnh — dữ liệu mẫu không có ảnh nào, nên body không dựng được.
        ActiveDescriptor descriptor = DescriptorTestKit.Load(DescriptorTestKit.Nova, root =>
        {
            ThanBodyChiDungKhiCoAnh(root);
            root["capability"]!["supportsImageToVideo"] = false;
        });

        DescriptorPreviewResult preview = DescriptorPreview.Render(
            descriptor.Descriptor,
            ProviderHostAllowlist.Parse("novagateway.net"));

        preview.Body.Should().BeNull();
        preview.Warnings.Should().Contain(w => w.Contains("submit.body không dựng được"));
    }
}
