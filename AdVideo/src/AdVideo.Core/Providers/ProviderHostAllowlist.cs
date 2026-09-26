namespace AdVideo.Core.Providers;

/// <summary>
/// Danh sách host mà hệ thống được phép gọi tới khi nói chuyện với provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nằm ở <c>SystemSetting</c>, KHÔNG nằm trong descriptor.</b> Nếu host hợp lệ khai trong chính
/// file JSON người vận hành dán vào thì người dán tự cấp quyền gọi ra bất cứ đâu — kể cả
/// <c>169.254.169.254</c>. Áp cho CẢ BA nguồn URL: <c>baseUrl</c>, URL provider trả về trong phản
/// hồi, và URL tải file.
/// </para>
/// <para>Cú pháp mỗi mục (cách nhau bằng dấu phẩy, chấm phẩy hoặc xuống dòng):</para>
/// <list type="bullet">
/// <item><c>api.elevenlabs.io</c> — https, cổng 443, đúng host đó.</item>
/// <item><c>*.fal.media</c> — https, cổng 443, mọi host CON (không gồm chính <c>fal.media</c>).</item>
/// <item><c>api.example.com:8443</c> — https, cổng chỉ định.</item>
/// <item><c>http://127.0.0.1:8080</c> — đúng scheme + host + cổng. Cách duy nhất để cho phép http,
/// dành cho engine tự host trong mạng nội bộ.</item>
/// </list>
/// <para>Danh sách rỗng = chặn hết (đóng khi lỗi).</para>
/// </remarks>
public sealed class ProviderHostAllowlist
{
    private readonly IReadOnlyList<Entry> _entries;

    private ProviderHostAllowlist(IReadOnlyList<Entry> entries, IReadOnlyList<string> invalid)
    {
        _entries = entries;
        InvalidEntries = invalid;
    }

    /// <summary>Mục không đọc được — bị bỏ qua, và nên được log để người vận hành sửa.</summary>
    public IReadOnlyList<string> InvalidEntries { get; }

    /// <summary>Số mục hợp lệ.</summary>
    public int Count => _entries.Count;

    public static ProviderHostAllowlist Parse(string? csv)
    {
        var entries = new List<Entry>();
        var invalid = new List<string>();

        foreach (string raw in (csv ?? "").Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseEntry(raw, out Entry? entry))
            {
                entries.Add(entry!);
            }
            else
            {
                invalid.Add(raw);
            }
        }

        return new ProviderHostAllowlist(entries, invalid);
    }

    public bool IsAllowed(Uri uri) => Explain(uri) is null;

    /// <summary>Lý do chặn, hoặc null nếu được phép.</summary>
    public string? Explain(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return $"URL \"{uri}\" không tuyệt đối.";
        }

        string host = uri.IdnHost.ToLowerInvariant();

        foreach (Entry entry in _entries)
        {
            if (string.Equals(uri.Scheme, entry.Scheme, StringComparison.OrdinalIgnoreCase)
                && uri.Port == entry.Port
                && (entry.Wildcard
                    ? host.Length > entry.Host.Length + 1 && host.EndsWith("." + entry.Host, StringComparison.Ordinal)
                    : host == entry.Host))
            {
                return null;
            }
        }

        return $"Host \"{uri.Scheme}://{uri.Authority}\" không nằm trong allowlist ProviderHostAllowlist. " +
               "Thêm host vào setting đó nếu đây là provider hợp lệ.";
    }

    private static bool TryParseEntry(string raw, out Entry? entry)
    {
        entry = null;

        if (raw.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.PathAndQuery != "/")
            {
                return false;
            }

            entry = new Entry(uri.Scheme, uri.IdnHost.ToLowerInvariant(), uri.Port, Wildcard: false);

            return true;
        }

        bool wildcard = raw.StartsWith("*.", StringComparison.Ordinal);
        string hostPort = wildcard ? raw[2..] : raw;

        if (!Uri.TryCreate("https://" + hostPort, UriKind.Absolute, out Uri? parsed)
            || parsed.PathAndQuery != "/"
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || (wildcard && parsed.HostNameType != UriHostNameType.Dns))
        {
            return false;
        }

        entry = new Entry("https", parsed.IdnHost.ToLowerInvariant(), parsed.Port, wildcard);

        return true;
    }

    private sealed record Entry(string Scheme, string Host, int Port, bool Wildcard);
}

/// <summary>
/// Request bị chặn vì host không nằm trong <see cref="ProviderHostAllowlist"/>.
/// </summary>
/// <remarks>
/// Cố ý KHÔNG kế thừa <see cref="HttpRequestException"/>: chỗ bắt lỗi mạng xếp lỗi đó vào loại
/// "thử lại được", mà thử lại một request bị chặn thì lần nào cũng bị chặn — và có thể đã trả tiền
/// render ở bước trước.
/// </remarks>
public sealed class ProviderHostBlockedException(string message) : Exception(message);
