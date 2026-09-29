using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;

namespace NewsCMS.Infrastructure.ImageStudio.Providers;

/// <summary>
/// API ảnh kiểu OpenAI: <c>POST {BaseUrl}/images/generations</c>. Dùng cho OpenAI, 9Router và các
/// gateway tương thích. <c>BaseUrl</c> theo đúng quy ước của kết nối chat (đã gồm <c>/v1</c>).
/// </summary>
public sealed class OpenAiImageProvider : IImageProvider
{
    public const string HttpClientName = "imagestudio-provider";

    /// <summary>Tham số riêng không được ghi đè: đổi chúng là đổi thứ người dùng đã chọn (và trả tiền).</summary>
    private static readonly HashSet<string> ProtectedParams = new(StringComparer.OrdinalIgnoreCase) { "model", "prompt", "n" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IImageDownloader _downloader;
    private readonly ILogger<OpenAiImageProvider> _logger;

    public OpenAiImageProvider(IHttpClientFactory httpClientFactory, IImageDownloader downloader, ILogger<OpenAiImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _downloader = downloader;
        _logger = logger;
    }

    public ImageProviderAdapter Adapter => ImageProviderAdapter.OpenAiImages;

    public async Task<ImageProviderResult> GenerateAsync(ImageGenerateRequest request, CancellationToken ct = default)
    {
        ImageProviderContext ctx = request.Context;
        var stopwatch = Stopwatch.StartNew();

        JsonObject body = BuildBody(request);
        string endpoint = $"{ctx.BaseUrl.TrimEnd('/')}/images/generations";

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan; // thời gian chờ theo từng model, đặt bằng CancellationToken bên dưới

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(ctx.TimeoutSeconds, 10, 900)));

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.ApiKey);

        HttpResponseMessage response;
        string raw;

        try
        {
            response = await client.SendAsync(message, timeout.Token);
            raw = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ImageProviderResult.Failure(ImageProviderErrorCodes.Timeout,
                $"Nhà cung cấp không trả ảnh sau {ctx.TimeoutSeconds} giây.", null, Elapsed(stopwatch));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Không gọi được API ảnh {Endpoint}: {Message}", endpoint, ProviderText.Redact(ex.Message, ctx.ApiKey));
            return ImageProviderResult.Failure(ImageProviderErrorCodes.ProviderError,
                "Không kết nối được tới nhà cung cấp tạo ảnh.", null, Elapsed(stopwatch));
        }

        using (response)
        {
            int status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                string detail = ProviderText.Redact(ProviderText.ExtractErrorMessage(raw), ctx.ApiKey, 300);
                string code = MapError(response.StatusCode, raw);
                _logger.LogWarning("API ảnh trả lỗi {Status} ({Code}): {Detail}", status, code, detail);

                return ImageProviderResult.Failure(code, FriendlyError(code, detail), status, Elapsed(stopwatch));
            }

            return await ReadImageAsync(raw, ctx, status, stopwatch, ct);
        }
    }

    /// <summary>Thân request: tham số chuẩn + tham số riêng quản trị khai ở model (giá trị null = bỏ tham số).</summary>
    public static JsonObject BuildBody(ImageGenerateRequest request)
    {
        ImageProviderContext ctx = request.Context;

        var body = new JsonObject
        {
            ["model"] = ctx.ModelId,
            ["prompt"] = request.Prompt,
            ["n"] = 1,
        };

        if (!string.IsNullOrWhiteSpace(request.Size))
        {
            body["size"] = request.Size;
        }

        if (!string.IsNullOrWhiteSpace(ctx.Quality))
        {
            body["quality"] = ctx.Quality;
        }

        if (!string.IsNullOrWhiteSpace(ctx.OutputFormat))
        {
            body["output_format"] = ctx.OutputFormat;
        }

        if (TryParseObject(ctx.ExtraParamsJson) is { } extra)
        {
            foreach ((string key, JsonNode? value) in extra.ToList())
            {
                if (ProtectedParams.Contains(key))
                {
                    continue;
                }

                if (value is null)
                {
                    body.Remove(key);
                }
                else
                {
                    body[key] = value.DeepClone();
                }
            }
        }

        return body;
    }

    /// <summary>Đã kiểm lúc lưu model; vẫn phòng dữ liệu cũ hỏng thay vì làm nổ cả job.</summary>
    private static JsonObject? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ImageProviderResult> ReadImageAsync(string raw, ImageProviderContext ctx, int status, Stopwatch stopwatch, CancellationToken ct)
    {
        JsonElement item;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                return Invalid("Nhà cung cấp không trả ảnh nào (thiếu data[0]).");
            }

            item = data[0].Clone();
        }
        catch (JsonException)
        {
            return Invalid("Nhà cung cấp trả về dữ liệu không đọc được.");
        }

        if (item.TryGetProperty("b64_json", out JsonElement b64) && b64.ValueKind == JsonValueKind.String)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(b64.GetString()!);
                return ImageProviderResult.Success(new ImageData(bytes, MimeFor(ctx.OutputFormat)), status, Elapsed(stopwatch));
            }
            catch (FormatException)
            {
                return Invalid("Ảnh base64 nhà cung cấp trả về bị hỏng.");
            }
        }

        if (item.TryGetProperty("url", out JsonElement url) && url.ValueKind == JsonValueKind.String)
        {
            string? trustedHost = Uri.TryCreate(ctx.BaseUrl, UriKind.Absolute, out Uri? baseUri) ? baseUri.Host : null;
            (ImageData? image, string? error) = await _downloader.DownloadAsync(url.GetString()!, trustedHost, ct);

            return image is not null
                ? ImageProviderResult.Success(image, status, Elapsed(stopwatch))
                : Invalid(error ?? "Không tải được ảnh kết quả.");
        }

        return Invalid("Nhà cung cấp không trả url hay b64_json.");

        ImageProviderResult Invalid(string message) =>
            ImageProviderResult.Failure(ImageProviderErrorCodes.InvalidResponse, message, status, Elapsed(stopwatch));
    }

    public static string MapError(HttpStatusCode status, string? body)
    {
        string text = body ?? string.Empty;

        if (text.Contains("content_policy", StringComparison.OrdinalIgnoreCase)
            || text.Contains("moderation", StringComparison.OrdinalIgnoreCase)
            || text.Contains("safety", StringComparison.OrdinalIgnoreCase))
        {
            return ImageProviderErrorCodes.ContentPolicy;
        }

        return (int)status switch
        {
            401 or 403 => ImageProviderErrorCodes.Auth,
            408 or 504 => ImageProviderErrorCodes.Timeout,
            429 => ImageProviderErrorCodes.RateLimited,
            >= 500 => ImageProviderErrorCodes.ProviderError,
            _ => ImageProviderErrorCodes.BadRequest,
        };
    }

    private static string FriendlyError(string code, string detail)
    {
        string message = code switch
        {
            ImageProviderErrorCodes.ContentPolicy => "Nhà cung cấp từ chối nội dung này (vi phạm chính sách). Hãy đổi mô tả.",
            ImageProviderErrorCodes.Auth => "API key của kết nối không hợp lệ hoặc không có quyền dùng model này.",
            ImageProviderErrorCodes.RateLimited => "Nhà cung cấp đang giới hạn tốc độ hoặc hết hạn mức. Thử lại sau ít phút.",
            ImageProviderErrorCodes.Timeout => "Nhà cung cấp xử lý quá lâu.",
            ImageProviderErrorCodes.ProviderError => "Nhà cung cấp tạo ảnh đang lỗi.",
            _ => "Nhà cung cấp từ chối yêu cầu.",
        };

        return string.IsNullOrWhiteSpace(detail) ? message : $"{message} ({detail})";
    }

    private static string MimeFor(string? format) => format?.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" => "image/jpeg",
        "webp" => "image/webp",
        _ => "image/png",
    };

    private static int Elapsed(Stopwatch stopwatch) => (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
}
