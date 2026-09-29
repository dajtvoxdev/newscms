using System.Net;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.ImageStudio.Providers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace NewsCMS.Tests.ImageStudio;

public class ImageStudioRulesTests
{
    private static readonly List<string> OpenAiSizes = ["1024x1024", "1536x1024", "1024x1536"];

    [Theory]
    [InlineData("1:1", "1024x1024")]
    [InlineData("16:9", "1536x1024")]
    [InlineData("4:3", "1536x1024")]
    [InlineData("9:16", "1024x1536")]
    [InlineData("3:4", "1024x1536")]
    public void ResolveSize_picks_nearest_supported_ratio(string aspect, string expected) =>
        Assert.Equal(expected, ImageStudioRules.ResolveSize(aspect, OpenAiSizes));

    [Fact]
    public void ResolveSize_keeps_ratio_strings_and_falls_back_to_auto()
    {
        Assert.Equal("16:9", ImageStudioRules.ResolveSize("16:9", ["1:1", "16:9", "9:16"]));
        Assert.Equal("auto", ImageStudioRules.ResolveSize("16:9", ["auto"]));
    }

    [Fact]
    public void ParseSizes_normalizes_and_reports_invalid()
    {
        List<string> sizes = ImageStudioRules.ParseSizes("1024x1024\n 16:9 ,AUTO; 1024x1024\nlarge\n0x10", out List<string> invalid);

        Assert.Equal(["1024x1024", "16:9", "auto"], sizes);
        Assert.Equal(["large", "0x10"], invalid);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", true)]
    [InlineData("http://localhost:20128/v1", true)]
    [InlineData("http://127.0.0.1:20128/v1", true)]
    [InlineData("http://api.example.com/v1", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not a url", false)]
    public void IsAllowedBaseUrl_requires_https_except_localhost(string url, bool allowed) =>
        Assert.Equal(allowed, ImageStudioRules.IsAllowedBaseUrl(url, out _));

    [Fact]
    public void BuildFinalPrompt_adds_purpose_brand_and_no_text_rule()
    {
        string prompt = ImageStudioRules.BuildFinalPrompt("  Cà phê sáng ở Hà Nội  ", ImagePurpose.PostCover, "tông xanh lá");

        Assert.StartsWith("Cà phê sáng ở Hà Nội", prompt);
        Assert.Contains("Ảnh bìa bài viết", prompt);
        Assert.Contains("Phong cách thương hiệu: tông xanh lá", prompt);
        Assert.Contains("Không chèn chữ", prompt);
    }

    [Fact]
    public void DefaultAltText_takes_first_sentence_and_caps_length()
    {
        Assert.Equal("Một ly cà phê muối trên bàn gỗ", ImageStudioRules.DefaultAltText("Một ly cà phê muối trên bàn gỗ. Ánh sáng ấm, phong cách tối giản."));
        Assert.True(ImageStudioRules.DefaultAltText(new string('a', 500)).Length <= 201);
    }
}

public class AiImageFinalizerTests
{
    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public void Finalize_writes_ai_label_that_survives_reencoding(string format, string mime)
    {
        FinalizedImage image = AiImageFinalizer.Finalize(ImageStudioTestHarness.Png(80, 40), format, AiImageFinalizer.TrainedAlgorithmicMedia);

        Assert.Equal(mime, image.MimeType);
        Assert.Equal((80, 40), (image.Width, image.Height));
        Assert.Equal(AiImageFinalizer.TrainedAlgorithmicMedia, AiImageFinalizer.ReadDigitalSourceType(image.Bytes));

        // Bước tối ưu ảnh của thư viện media nạp rồi lưu lại bằng ImageSharp — nhãn phải còn.
        using Image reloaded = Image.Load(image.Bytes);
        using var again = new MemoryStream();
        reloaded.Save(again, reloaded.Metadata.DecodedImageFormat!);
        Assert.Equal(AiImageFinalizer.TrainedAlgorithmicMedia, AiImageFinalizer.ReadDigitalSourceType(again.ToArray()));
    }

    [Fact]
    public void Finalize_drops_metadata_sent_by_provider()
    {
        using var source = new Image<Rgba32>(20, 20);
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Artist, "tai-khoan-noi-bo-cua-provider");
        using var stream = new MemoryStream();
        source.SaveAsPng(stream);

        FinalizedImage image = AiImageFinalizer.Finalize(stream.ToArray(), "png", AiImageFinalizer.TrainedAlgorithmicMedia);

        ExifProfile? exif = Image.Identify(image.Bytes).Metadata.ExifProfile;
        Assert.NotNull(exif);
        Assert.False(exif!.TryGetValue(ExifTag.Artist, out _));
        Assert.True(exif.TryGetValue(ExifTag.Software, out IExifValue<string>? software));
        Assert.Equal(AiImageFinalizer.SoftwareName, software!.Value);
    }

    [Fact]
    public void Finalize_rejects_non_image() =>
        Assert.Throws<InvalidDataException>(() => AiImageFinalizer.Finalize("<html>không phải ảnh</html>"u8.ToArray(), "png", AiImageFinalizer.TrainedAlgorithmicMedia));
}

public class ProviderTextTests
{
    [Fact]
    public void Redact_hides_keys_bearer_tokens_and_base64()
    {
        string key = "sk-proj-abcdefghijklmnopqrstuvwxyz0123";
        string raw = $"Invalid key {key}. Header: Bearer abcdefghijklmnop. api_key=zzzzzzzz9999 data {new string('A', 300)}";

        string redacted = ProviderText.Redact(raw, key);

        Assert.DoesNotContain(key, redacted);
        Assert.DoesNotContain("abcdefghijklmnop", redacted);
        Assert.DoesNotContain("zzzzzzzz9999", redacted);
        Assert.DoesNotContain(new string('A', 300), redacted);
        Assert.Contains("[base64]", redacted);
    }

    [Fact]
    public void Redact_caps_length()
    {
        Assert.True(ProviderText.Redact(new string('x', 2000) + " ", null, 100).Length <= 102);
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"Your request was rejected\",\"code\":\"content_policy_violation\"}}", "Your request was rejected")]
    [InlineData("{\"error\":\"quota exceeded\"}", "quota exceeded")]
    [InlineData("{\"detail\":\"bad size\"}", "bad size")]
    [InlineData("plain text", "plain text")]
    public void ExtractErrorMessage_reads_common_shapes(string body, string expected) =>
        Assert.Equal(expected, ProviderText.ExtractErrorMessage(body));
}

public class ImageDownloadGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.5", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void IsBlocked_covers_private_ranges(string ip, bool blocked) =>
        Assert.Equal(blocked, ImageDownloadGuard.IsBlocked(IPAddress.Parse(ip)));

    [Fact]
    public async Task Download_rejects_http_and_private_literal_hosts_before_any_request()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var guard = new ImageDownloadGuard(new StubHttpClientFactory(handler));

        (var image1, string? error1) = await guard.DownloadAsync("http://cdn.example.com/a.png", trustedHost: null);
        (var image2, string? error2) = await guard.DownloadAsync("https://169.254.169.254/latest/meta-data", trustedHost: null);

        Assert.Null(image1);
        Assert.Contains("https", error1);
        Assert.Null(image2);
        Assert.Contains("nội bộ", error2);
        Assert.Empty(handler.Seen);
    }

    [Fact]
    public async Task Download_allows_http_for_trusted_host_and_validates_content()
    {
        byte[] png = ImageStudioTestHarness.Png();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith(".png")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") });
        var guard = new ImageDownloadGuard(new StubHttpClientFactory(handler));

        (var ok, _) = await guard.DownloadAsync("http://localhost:20128/files/a.png", trustedHost: "localhost");
        (var notImage, string? error) = await guard.DownloadAsync("https://cdn.example.com/page.html", trustedHost: null);

        Assert.NotNull(ok);
        Assert.Equal("image/png", ok!.MimeType);
        Assert.Null(notImage);
        Assert.Contains("không phải ảnh", error);
    }

    [Fact]
    public async Task Download_rejects_redirect_and_oversized_content()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath == "/redirect"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://10.0.0.1/") } }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[16]) { Headers = { ContentLength = ImageDownloadGuard.MaxBytes + 1 } } });
        var guard = new ImageDownloadGuard(new StubHttpClientFactory(handler));

        (var redirected, string? redirectError) = await guard.DownloadAsync("https://cdn.example.com/redirect", null);
        (var big, string? bigError) = await guard.DownloadAsync("https://cdn.example.com/big.png", null);

        Assert.Null(redirected);
        Assert.Contains("302", redirectError);
        Assert.Null(big);
        Assert.Contains("20 MB", bigError);
    }
}
