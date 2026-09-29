using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using NewsCMS.Application.ImageStudio.Providers;
using SixLabors.ImageSharp;

namespace NewsCMS.Infrastructure.ImageStudio.Imaging;

/// <summary>Tải ảnh từ một URL do provider trả về.</summary>
public interface IImageDownloader
{
    /// <param name="trustedHost">
    /// Host của kết nối do quản trị nhập (ví dụ 9Router chạy ở <c>localhost</c>). Chỉ host này được đi
    /// vào địa chỉ nội bộ và được dùng http.
    /// </param>
    Task<(ImageData? Image, string? Error)> DownloadAsync(string url, string? trustedHost, CancellationToken ct = default);
}

/// <summary>
/// Tải ảnh kết quả mà không mở đường SSRF. URL do provider (bên ngoài) đưa, nên không được tin:
/// <list type="bullet">
/// <item>chỉ https — trừ host của chính kết nối quản trị đã nhập;</item>
/// <item>chặn IP nội bộ/loopback/link-local <b>tại lúc mở socket</b> (sau khi phân giải DNS), nên đổi
/// DNS giữa lúc kiểm và lúc kết nối cũng không lọt;</item>
/// <item>không theo redirect; tối đa 20 MB; phải giải mã được thành ảnh.</item>
/// </list>
/// </summary>
public sealed class ImageDownloadGuard : IImageDownloader
{
    public const string HttpClientName = "imagestudio-download";
    public const long MaxBytes = 20L * 1024 * 1024;

    private static readonly HttpRequestOptionsKey<string> TrustedHostKey = new("imagestudio.trusted-host");

    private readonly IHttpClientFactory _httpClientFactory;

    public ImageDownloadGuard(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    /// <summary>Handler chính của HttpClient tải ảnh: tắt redirect, kiểm IP ngay lúc kết nối.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = ConnectAsync,
    };

    public async Task<(ImageData? Image, string? Error)> DownloadAsync(string url, string? trustedHost, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return (null, "Nhà cung cấp trả về đường dẫn ảnh không hợp lệ.");
        }

        bool trusted = trustedHost is not null && string.Equals(uri.Host, trustedHost, StringComparison.OrdinalIgnoreCase);

        if (uri.Scheme != Uri.UriSchemeHttps && !(trusted && uri.Scheme == Uri.UriSchemeHttp))
        {
            return (null, "Nhà cung cấp trả về đường dẫn ảnh không phải https.");
        }

        // Kiểm sớm IP viết thẳng trong URL — ConnectCallback cũng chặn, nhưng báo lỗi ở đây rõ hơn.
        if (!trusted && IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? literal) && IsBlocked(literal))
        {
            return (null, "Đường dẫn ảnh trỏ vào địa chỉ nội bộ — đã chặn.");
        }

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(60);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        if (trusted)
        {
            request.Options.Set(TrustedHostKey, uri.Host);
        }

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (null, $"Tải ảnh kết quả lỗi HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return (null, "Ảnh kết quả quá lớn (trên 20 MB).");
            }

            byte[] bytes = await ReadLimitedAsync(response.Content, ct);

            if (bytes.Length == 0)
            {
                return (null, "Ảnh kết quả rỗng.");
            }

            if (bytes.Length > MaxBytes)
            {
                return (null, "Ảnh kết quả quá lớn (trên 20 MB).");
            }

            // Không tin Content-Type: phải giải mã được mới là ảnh.
            try
            {
                ImageInfo info = Image.Identify(bytes);
                string mime = info.Metadata.DecodedImageFormat?.DefaultMimeType ?? "application/octet-stream";
                return (new ImageData(bytes, mime), null);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
            {
                return (null, "Nội dung tải về không phải ảnh.");
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            return (null, "Đường dẫn ảnh trỏ vào địa chỉ nội bộ — đã chặn.");
        }
        catch (HttpRequestException)
        {
            return (null, "Không tải được ảnh kết quả từ nhà cung cấp.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "Tải ảnh kết quả quá thời gian.");
        }
    }

    /// <summary>Địa chỉ không được phép kết nối tới (trừ host tin cậy).</summary>
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();

            return b[0] == 0                                   // 0.0.0.0/8
                || b[0] == 10                                  // 10/8
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64/10 (CGNAT)
                || b[0] == 127                                 // 127/8
                || (b[0] == 169 && b[1] == 254)                // link-local, metadata cloud
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12
                || (b[0] == 192 && b[1] == 168)                // 192.168/16
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0/24
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18/15
                || b[0] >= 224;                                // multicast + reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] b = address.GetAddressBytes();

            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
        }

        return true;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        string host = context.DnsEndPoint.Host;
        bool trusted = context.InitialRequestMessage.Options.TryGetValue(TrustedHostKey, out string? trustedHost)
                       && string.Equals(trustedHost, host, StringComparison.OrdinalIgnoreCase);

        IPAddress[] addresses = IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        IPAddress[] allowed = trusted ? addresses : addresses.Where(a => !IsBlocked(a)).ToArray();

        if (allowed.Length == 0)
        {
            throw new BlockedAddressException(host);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > MaxBytes)
            {
                break;
            }
        }

        return buffer.ToArray();
    }

    private sealed class BlockedAddressException(string host) : Exception($"Blocked address for host {host}");
}
