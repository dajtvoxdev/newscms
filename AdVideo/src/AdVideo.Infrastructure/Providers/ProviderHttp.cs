using System.Net.Http.Json;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Dựng request gọi provider với header xác thực gắn theo TỪNG REQUEST, và chỉ khi đúng origin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Không mutate <c>DefaultRequestHeaders</c>.</b> Header nằm trên client thì đi theo MỌI request
/// của client đó — kể cả GET tới một URL lấy từ thân phản hồi của provider. Đó chính là đường key
/// fal đi tới host lạ (kế hoạch provider khai báo, mục 1.7).
/// </para>
/// <para>
/// <b>Cùng origin mới gắn key:</b> scheme + host + cổng phải trùng endpoint đã cấu hình. URL trỏ
/// sang host khác (CDN, link đã ký) vẫn được gọi nếu nằm trong allowlist, nhưng không mang key.
/// </para>
/// </remarks>
public static class ProviderHttp
{
    public static bool IsSameOrigin(Uri a, Uri b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;
    }

    /// <summary>Request có key nếu <paramref name="url"/> cùng origin với <paramref name="trustedBase"/>.</summary>
    public static HttpRequestMessage Create(
        HttpMethod method,
        string url,
        Uri trustedBase,
        string headerName,
        string headerValue,
        object? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        if (IsSameOrigin(request.RequestUri!, trustedBase))
        {
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        return request;
    }
}
