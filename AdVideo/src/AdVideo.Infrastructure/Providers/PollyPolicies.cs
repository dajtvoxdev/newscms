using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Chính sách thử lại ở tầng HTTP cho mọi lời gọi provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Không thử lại POST 5xx.</b> Đây là điểm khác biệt quan trọng nhất so với một chính sách
/// retry mặc định. POST gửi yêu cầu sinh video là thao tác TỐN TIỀN và không idempotent: 502 có
/// thể là proxy chết sau khi hàng đợi đã nhận việc. Thử lại nghĩa là trả tiền hai lần cho một
/// shot mà không có cách nào biết được từ phía mình. Thất bại ở đó được đẩy lên tầng trên, nơi
/// trạng thái shot trong DB quyết định có gọi lại hay không.
/// </para>
/// <para>
/// <b>429 thì luôn thử lại, kể cả POST.</b> Bị giới hạn tốc độ nghĩa là yêu cầu chưa được nhận —
/// không có gì để trả tiền, nên gửi lại là an toàn.
/// </para>
/// <para>
/// <b>Lỗi tầng vận chuyển (<see cref="HttpRequestException"/>) KHÔNG được thử lại ở đây</b>, vì
/// cùng lý do: đứt kết nối giữa chừng không cho biết máy chủ đã nhận yêu cầu hay chưa, và
/// exception thì không mang theo phương thức để phân biệt. Nó nổi lên cho bước gọi xử lý, nơi có
/// đủ ngữ cảnh về job và shot.
/// </para>
/// <para>
/// <b>Tôn trọng Retry-After.</b> Chờ theo cấp số nhân trong khi nhà cung cấp đã nói rõ phải chờ
/// bao lâu là cách nhanh nhất để bị chặn lâu hơn.
/// </para>
/// </remarks>
public static class PollyPolicies
{
    private const int DefaultRetryCount = 3;

    /// <summary>Khoảng chờ tối đa một lần, giây. Dài hơn thì job đứng im lâu hơn mức người dùng chịu được.</summary>
    private const int MaxDelaySeconds = 60;

    public static IAsyncPolicy<HttpResponseMessage> ProviderRetry(ILogger logger, int retryCount = DefaultRetryCount)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return Policy<HttpResponseMessage>
            .HandleResult(ShouldRetry)
            .WaitAndRetryAsync(
                retryCount,
                SleepDuration,
                (outcome, delay, attempt, _) => OnRetry(logger, outcome, delay, attempt));
    }

    /// <summary>
    /// Một phản hồi có ĐÁNG thử lại không, xét cả mã trạng thái lẫn phương thức.
    /// </summary>
    /// <remarks>
    /// Là điều kiện lọc của chính sách bên trên, để tách được "đáng thử lại" khỏi "chờ bao lâu" —
    /// và để lớp gọi dùng chung đúng một luật thay vì chép lại.
    /// </remarks>
    public static bool ShouldRetry(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        bool isReadOnly = response.RequestMessage?.Method is { } method
            && (method == HttpMethod.Get || method == HttpMethod.Head);

        return isReadOnly
            && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout);
    }

    private static TimeSpan SleepDuration(int attempt, DelegateResult<HttpResponseMessage> outcome, Context context)
    {
        // Nhà cung cấp nói chờ bao lâu thì chờ đúng bấy nhiêu.
        if (outcome.Result is { } response
            && ProviderFailureMapper.ReadRetryAfter(response) is { } seconds
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelaySeconds));
        }

        // 2s, 4s, 8s — cộng nhiễu để nhiều shot của cùng một job không đập lại vào API cùng lúc.
        double backoff = Math.Min(Math.Pow(2, attempt), MaxDelaySeconds);

        return TimeSpan.FromSeconds(backoff + Random.Shared.NextDouble());
    }

    private static Task OnRetry(
        ILogger logger,
        DelegateResult<HttpResponseMessage> outcome,
        TimeSpan delay,
        int attempt)
    {
        string reason = outcome.Result is { } response
            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
            : outcome.Exception?.GetType().Name ?? "không rõ";

        logger.LogWarning(
            "Gọi provider thất bại ({Reason}), thử lại lần {Attempt} sau {Delay:0.#} giây.",
            reason,
            attempt,
            delay.TotalSeconds);

        return Task.CompletedTask;
    }
}
