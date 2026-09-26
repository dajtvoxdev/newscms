using System.Net;
using System.Text.Json;
using AdVideo.Core.Providers;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Quy mã lỗi HTTP của provider về <see cref="VideoFailureKind"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phân loại quan trọng đến thế:</b> <c>VideoResult.CanRetry</c> đọc trực tiếp từ đây.
/// Phân loại một lỗi nội dung thành Transient là làm hệ thống gửi lại đúng prompt bị từ chối
/// thêm hai lần nữa — chậm hơn, và với một số provider là tốn tiền ba lần cho ba lần bị từ chối.
/// Ngược lại, phân loại 429 thành ContentRejected là bỏ cuộc trong khi chỉ cần chờ vài giây.
/// </para>
/// <para>
/// <b>422 là mã đặc biệt.</b> fal.ai (và nhiều cổng khác) dùng 422 cho "tham số không hợp lệ", trong
/// đó có cả trường hợp prompt vi phạm chính sách nội dung. Thân phản hồi là thứ duy nhất phân
/// biệt được hai loại, nên phải đọc nó chứ không chỉ nhìn mã.
/// </para>
/// <para>
/// <b>Mã lỗi trong thân thắng marker chuỗi.</b> Provider đặt mã máy đọc được ở <c>error.code</c>,
/// <c>detail.status</c>, <c>detail[].type</c>… Đọc mã trước rồi mới dò chữ, vì dò chữ trượt ở
/// những chỗ ngớ ngẩn nhất: <c>content_policy</c> (gạch dưới) không khớp marker
/// <c>"content policy"</c> (dấu cách), và job bị kiểm duyệt từng bị báo thành lỗi không rõ — tức
/// là được retry.
/// </para>
/// </remarks>
public static class ProviderFailureMapper
{
    private static readonly string[] ContentPolicyMarkers =
    [
        "content policy",
        "safety",
        "prohibited",
        "blocked",
        "moderation",
        "nsfw",
        "violat",
        "sensitive",
    ];

    /// <summary>Tên khoá có thể mang mã lỗi máy đọc được, ở bất kỳ cấp nào trong ba cấp đầu.</summary>
    private static readonly string[] CodeKeys = ["code", "type", "status", "reason", "error_code", "error_type"];

    /// <summary>
    /// Mã lỗi đã biết → loại lỗi. So sánh không phân biệt hoa thường.
    /// </summary>
    /// <remarks>
    /// Hết tiền/hết hạn mức là <see cref="VideoFailureKind.ProviderUnavailable"/>, không phải
    /// <see cref="VideoFailureKind.Unknown"/>: Unknown retry được, và thử lại một tài khoản hết tiền
    /// thì lần nào cũng hỏng.
    /// </remarks>
    private static readonly Dictionary<string, VideoFailureKind> KnownCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["content_policy"] = VideoFailureKind.ContentRejected,
        ["content_policy_violation"] = VideoFailureKind.ContentRejected,
        ["content_filter"] = VideoFailureKind.ContentRejected,
        ["content_moderation"] = VideoFailureKind.ContentRejected,
        ["moderation_blocked"] = VideoFailureKind.ContentRejected,
        ["safety_violation"] = VideoFailureKind.ContentRejected,
        ["nsfw"] = VideoFailureKind.ContentRejected,
        ["nsfw_content"] = VideoFailureKind.ContentRejected,

        ["rate_limit"] = VideoFailureKind.RateLimited,
        ["rate_limited"] = VideoFailureKind.RateLimited,
        ["rate_limit_exceeded"] = VideoFailureKind.RateLimited,
        ["too_many_requests"] = VideoFailureKind.RateLimited,
        ["too_many_concurrent_requests"] = VideoFailureKind.RateLimited,

        ["insufficient_quota"] = VideoFailureKind.ProviderUnavailable,
        ["insufficient_balance"] = VideoFailureKind.ProviderUnavailable,
        ["insufficient_credits"] = VideoFailureKind.ProviderUnavailable,
        ["quota_exceeded"] = VideoFailureKind.ProviderUnavailable,
        ["payment_required"] = VideoFailureKind.ProviderUnavailable,
        ["invalid_api_key"] = VideoFailureKind.ProviderUnavailable,
        ["unauthorized"] = VideoFailureKind.ProviderUnavailable,
    };

    public static VideoFailureKind FromStatus(HttpStatusCode status, string? body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return VideoFailureKind.RateLimited;
        }

        if (FromErrorCode(body) is { } byCode)
        {
            return byCode;
        }

        if (status == HttpStatusCode.PaymentRequired)
        {
            // Hết tiền. Trước đây rơi xuống Unknown — mà Unknown retry được, nên hệ thống thử lại
            // đủ số lần rồi mới bỏ. Thử lại không nạp thêm tiền vào tài khoản.
            return VideoFailureKind.ProviderUnavailable;
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Key sai hoặc hết hạn. KHÔNG phải lỗi tạm thời: thử lại với đúng key sai đó thì lần
            // nào cũng hỏng, chỉ tốn thêm thời gian của job.
            return VideoFailureKind.ProviderUnavailable;
        }

        if (status is HttpStatusCode.BadRequest
            or HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.UnsupportedMediaType)
        {
            return LooksLikeContentPolicy(body)
                ? VideoFailureKind.ContentRejected
                : VideoFailureKind.Unknown;
        }

        if (status is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
        {
            return VideoFailureKind.Transient;
        }

        if ((int)status >= 500)
        {
            return VideoFailureKind.ProviderUnavailable;
        }

        return VideoFailureKind.Unknown;
    }

    /// <summary>
    /// Phân loại một job mà provider báo hỏng trong thân phản hồi 200 (hàng đợi báo FAILED).
    /// </summary>
    /// <remarks>
    /// Mặc định là <see cref="VideoFailureKind.ProviderUnavailable"/>: hàng đợi báo hỏng mà không
    /// nói vì sao thì là sự cố phía họ, không phải lỗi tạm thời để thử lại ngay.
    /// </remarks>
    public static VideoFailureKind FromFailedJobBody(string? body)
    {
        if (FromErrorCode(body) is { } byCode)
        {
            return byCode;
        }

        return LooksLikeContentPolicy(body)
            ? VideoFailureKind.ContentRejected
            : VideoFailureKind.ProviderUnavailable;
    }

    /// <summary>
    /// Đọc mã lỗi máy đọc được trong thân JSON. Null nếu thân không phải JSON hoặc không có mã nào đã biết.
    /// </summary>
    public static VideoFailureKind? FromErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart() is not ['{' or '[', ..])
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return FindKnownCode(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool LooksLikeContentPolicy(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        // Chuẩn hoá gạch dưới và gạch ngang thành dấu cách: "content_policy" và "content-policy"
        // phải khớp cùng marker với "content policy".
        string normalized = body.Replace('_', ' ').Replace('-', ' ');

        foreach (string marker in ContentPolicyMarkers)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static VideoFailureKind? FindKnownCode(JsonElement element, int depth)
    {
        // Ba cấp là đủ cho mọi hình dạng đã gặp: {error:{code}}, {detail:{status}}, {detail:[{type}]}.
        // Đi sâu hơn là bắt đầu đọc nhầm dữ liệu dội lại từ request.
        if (depth > 3)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && CodeKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                        && property.Value.GetString() is { } code
                        && KnownCodes.TryGetValue(code.Trim(), out VideoFailureKind kind))
                    {
                        return kind;
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        && FindKnownCode(property.Value, depth + 1) is { } nested)
                    {
                        return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (FindKnownCode(item, depth + 1) is { } nested)
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    /// <summary>Đọc <c>Retry-After</c>, chấp nhận cả dạng số giây lẫn dạng mốc thời gian HTTP.</summary>
    public static int? ReadRetryAfter(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        System.Net.Http.Headers.RetryConditionHeaderValue? header = response.Headers.RetryAfter;

        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return (int)Math.Ceiling(delta.TotalSeconds);
        }

        if (header.Date is { } date)
        {
            double seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;

            return seconds > 0 ? (int)Math.Ceiling(seconds) : 1;
        }

        return null;
    }
}
