using System.Net;
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
/// <b>422 là mã đặc biệt.</b> Cả fal.ai lẫn Gemini đều dùng 422 cho "tham số không hợp lệ", trong
/// đó có cả trường hợp prompt vi phạm chính sách nội dung. Thân phản hồi là thứ duy nhất phân
/// biệt được hai loại, nên phải đọc nó chứ không chỉ nhìn mã.
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

    public static VideoFailureKind FromStatus(HttpStatusCode status, string? body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return VideoFailureKind.RateLimited;
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

    public static bool LooksLikeContentPolicy(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        foreach (string marker in ContentPolicyMarkers)
        {
            if (body.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
