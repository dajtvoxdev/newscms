using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.ImageStudio.Providers;

namespace NewsCMS.Tests.ImageStudio;

public class OpenAiImageProviderTests
{
    private const string ApiKey = "sk-live-secret-abcdefghijklmnop1234";

    private static ImageProviderContext Context(string baseUrl = "https://gateway.example.com/v1", string? quality = "high", string? extra = null) =>
        new(baseUrl, ApiKey, "gpt-image-1", quality, "png", 60, extra);

    private static (OpenAiImageProvider Provider, StubHandler Handler, FakeDownloader Downloader) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var downloader = new FakeDownloader();
        var provider = new OpenAiImageProvider(new StubHttpClientFactory(handler), downloader, NullLogger<OpenAiImageProvider>.Instance);
        return (provider, handler, downloader);
    }

    private static string B64Response() =>
        JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(ImageStudioTestHarness.Png()) } } });

    [Fact]
    public async Task Generate_posts_one_image_request_with_bearer_key()
    {
        var (provider, handler, _) = Create(_ => StubHandler.Json(HttpStatusCode.OK, B64Response()));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(Context(), "Một quả táo", "1536x1024"));

        Assert.True(result.Ok);
        Assert.Equal("image/png", result.Image!.MimeType);
        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("https://gateway.example.com/v1/images/generations", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ApiKey, request.Headers.Authorization.Parameter);

        JsonObject json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("gpt-image-1", (string?)json["model"]);
        Assert.Equal("Một quả táo", (string?)json["prompt"]);
        Assert.Equal(1, (int?)json["n"]);
        Assert.Equal("1536x1024", (string?)json["size"]);
        Assert.Equal("high", (string?)json["quality"]);
        Assert.Equal("png", (string?)json["output_format"]);
    }

    [Fact]
    public void BuildBody_merges_extra_params_but_protects_model_prompt_and_n()
    {
        var request = new ImageGenerateRequest(
            Context(extra: "{\"background\":\"transparent\",\"output_format\":null,\"model\":\"hack\",\"n\":10,\"prompt\":\"x\"}"),
            "Một quả táo",
            "1024x1024");

        JsonObject body = OpenAiImageProvider.BuildBody(request);

        Assert.Equal("transparent", (string?)body["background"]);
        Assert.False(body.ContainsKey("output_format"));
        Assert.Equal("gpt-image-1", (string?)body["model"]);
        Assert.Equal(1, (int?)body["n"]);
        Assert.Equal("Một quả táo", (string?)body["prompt"]);
    }

    [Fact]
    public async Task Generate_downloads_url_results_trusting_only_the_connection_host()
    {
        var (provider, _, downloader) = Create(_ => StubHandler.Json(HttpStatusCode.OK, "{\"data\":[{\"url\":\"http://localhost:20128/out/a.png\"}]}"));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(Context("http://localhost:20128/v1"), "táo", "1024x1024"));

        Assert.True(result.Ok);
        Assert.Equal(("http://localhost:20128/out/a.png", "localhost"), Assert.Single(downloader.Calls));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Rejected by safety system\",\"code\":\"content_policy_violation\"}}", ImageProviderErrorCodes.ContentPolicy)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Incorrect API key provided: sk-live-secret-abcdefghijklmnop1234\"}}", ImageProviderErrorCodes.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Rate limit\"}}", ImageProviderErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, "upstream error", ImageProviderErrorCodes.ProviderError)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid size\"}}", ImageProviderErrorCodes.BadRequest)]
    public async Task Generate_maps_errors_and_never_leaks_the_key(HttpStatusCode status, string body, string expectedCode)
    {
        var (provider, _, _) = Create(_ => StubHandler.Json(status, body));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(Context(), "táo", "1024x1024"));

        Assert.False(result.Ok);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.DoesNotContain(ApiKey, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"b64_json\":\"%%%không-phải-base64\"}]}")]
    [InlineData("not json")]
    public async Task Generate_reports_invalid_responses(string body)
    {
        var (provider, _, _) = Create(_ => StubHandler.Json(HttpStatusCode.OK, body));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(Context(), "táo", "1024x1024"));

        Assert.False(result.Ok);
        Assert.Equal(ImageProviderErrorCodes.InvalidResponse, result.ErrorCode);
    }

    [Fact]
    public async Task Generate_turns_network_failure_into_provider_error()
    {
        var (provider, _, _) = Create(_ => throw new HttpRequestException("connection refused"));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(Context(), "táo", "1024x1024"));

        Assert.False(result.Ok);
        Assert.Equal(ImageProviderErrorCodes.ProviderError, result.ErrorCode);
        Assert.Null(result.HttpStatus);
    }

    private sealed class FakeDownloader : IImageDownloader
    {
        public List<(string Url, string? TrustedHost)> Calls { get; } = [];

        public Task<(ImageData? Image, string? Error)> DownloadAsync(string url, string? trustedHost, CancellationToken ct = default)
        {
            Calls.Add((url, trustedHost));
            return Task.FromResult<(ImageData?, string?)>((new ImageData(ImageStudioTestHarness.Png(), "image/png"), null));
        }
    }
}
