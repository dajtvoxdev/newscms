using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewsCMS.Application.Common;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>Địa chỉ + key để gọi AdVideo. Chỉ sống trong bộ nhớ; <see cref="ToString"/> bị chặn để không lọt key vào log.</summary>
public sealed record AdVideoEndpoint(string BaseUrl, string Key, string HeaderName, int TimeoutSeconds)
{
    public override string ToString() => $"{BaseUrl} ({HeaderName})";
}

/// <summary>
/// Gửi request tới AdVideo và đổi mọi kiểu hỏng thành <see cref="Result"/> đọc được.
/// </summary>
/// <remarks>
/// <para>
/// <b>Không ném exception ra trang admin.</b> AdVideo tắt, sai địa chỉ, sai key, timeout — tất cả đều
/// thành một câu tiếng Việt nói rõ phải sửa gì. Trang admin hiện câu đó trong thông báo đỏ, không
/// hiện trang lỗi 500.
/// </para>
/// <para>
/// Lỗi của AdVideo đi dạng <c>application/problem+json</c>: <c>title</c>, <c>detail</c>, và
/// <c>errors</c> — hoặc object <c>{trường: [lỗi]}</c> (lỗi kiểm dữ liệu) hoặc mảng lỗi. Gộp cả
/// ba lại, vì AdVideo trả hết lỗi trong một lượt và người sửa cần thấy hết.
/// </para>
/// </remarks>
public static class AdVideoHttp
{
    public const string HttpClientName = "advideo";
    public const string TenantKeyHeader = "X-AdVideo-Key";
    public const string OperatorKeyHeader = "X-AdVideo-Operator-Key";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static HttpClient CreateClient(IHttpClientFactory factory, AdVideoEndpoint endpoint)
    {
        HttpClient client = factory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(endpoint.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(endpoint.TimeoutSeconds, 5, 600));
        client.DefaultRequestHeaders.Add(endpoint.HeaderName, endpoint.Key);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    public static HttpContent JsonBody(object body) =>
        new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

    /// <summary>Gửi và đọc thân JSON thành <typeparamref name="T"/>.</summary>
    public static async Task<Result<T>> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, HttpContent? content, CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        (HttpResponseMessage? response, string? failure) = await TrySendAsync(client, method, path, content, headers, ct);

        if (response is null)
        {
            return Result<T>.Failure(failure!);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return Result<T>.Failure(DescribeFailure(response.StatusCode, body, client.BaseAddress));
            }

            try
            {
                T? value = JsonSerializer.Deserialize<T>(body, Json);

                return value is null
                    ? Result<T>.Failure("AdVideo trả phản hồi rỗng.")
                    : Result<T>.Success(value);
            }
            catch (JsonException ex)
            {
                return Result<T>.Failure($"Không đọc được phản hồi của AdVideo ({ex.Message}). Hai bên có thể đang lệch phiên bản.");
            }
        }
    }

    /// <summary>Gửi, không cần thân phản hồi (204).</summary>
    public static async Task<Result> SendAsync(
        HttpClient client, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        (HttpResponseMessage? response, string? failure) = await TrySendAsync(client, method, path, content, null, ct);

        if (response is null)
        {
            return Result.Failure(failure!);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            string body = await response.Content.ReadAsStringAsync(ct);

            return Result.Failure(DescribeFailure(response.StatusCode, body, client.BaseAddress));
        }
    }

    private static async Task<(HttpResponseMessage? Response, string? Failure)> TrySendAsync(
        HttpClient client, HttpMethod method, string path, HttpContent? content,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };

        if (headers is not null)
        {
            foreach ((string name, string value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            return (await client.SendAsync(request, ct), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, $"AdVideo ({client.BaseAddress}) không trả lời trong {client.Timeout.TotalSeconds:0} giây.");
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Không kết nối được AdVideo tại {client.BaseAddress}: {ex.Message}. Kiểm tra địa chỉ trong Cấu hình AdVideo và dịch vụ có đang chạy.");
        }
    }

    /// <summary>Đổi phản hồi lỗi của AdVideo thành một câu người đọc được.</summary>
    public static string DescribeFailure(HttpStatusCode status, string body, Uri? baseAddress)
    {
        string? title = null;
        string? detail = null;
        var items = new List<string>();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                title = Text(root, "title");
                detail = Text(root, "detail");

                if (root.TryGetProperty("errors", out JsonElement errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array)
                    {
                        items.AddRange(errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                    }
                    else if (errors.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty field in errors.EnumerateObject())
                        {
                            IEnumerable<string> messages = field.Value.ValueKind == JsonValueKind.Array
                                ? field.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                                : [];

                            items.AddRange(messages.Select(m => $"{field.Name}: {m}"));
                        }
                    }
                }

                if (root.TryGetProperty("missing_characters", out JsonElement missing) && missing.ValueKind == JsonValueKind.Array
                    && missing.GetArrayLength() > 0)
                {
                    items.Add("Ký tự thiếu: " + string.Concat(missing.EnumerateArray().Select(e => e.GetString())));
                }
            }
        }
        catch (JsonException)
        {
            // Thân không phải JSON (proxy trả trang HTML lỗi…) — chỉ còn mã trạng thái để nói.
        }

        string prefix = status switch
        {
            HttpStatusCode.Unauthorized =>
                "AdVideo từ chối key (401). Kiểm tra operator key trong Cấu hình AdVideo, hoặc cấp lại key cho site.",
            HttpStatusCode.NotFound when title is null =>
                $"Không tìm thấy trên AdVideo (404) — địa chỉ {baseAddress} có đúng là AdVideo.Api không?",
            _ => title ?? $"AdVideo trả lỗi {(int)status}.",
        };

        var parts = new List<string> { prefix };

        if (!string.IsNullOrWhiteSpace(detail) && !string.Equals(detail, prefix, StringComparison.Ordinal)
            && !(items.Count > 0 && detail.Contains("xem danh sách errors", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add(detail);
        }

        parts.AddRange(items);

        return string.Join(" — ", parts.Distinct());
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
