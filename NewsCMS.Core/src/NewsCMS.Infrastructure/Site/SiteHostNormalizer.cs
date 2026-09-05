namespace NewsCMS.Infrastructure.Site;

/// <summary>Chuẩn hóa host để tra cứu site: bỏ scheme/port, lowercase, trim.</summary>
public static class SiteHostNormalizer
{
    public static string Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var h = host.Trim().ToLowerInvariant();

        // Bỏ scheme nếu vô tình dán cả URL.
        var scheme = h.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) h = h[(scheme + 3)..];

        // Bỏ path.
        var slash = h.IndexOf('/');
        if (slash >= 0) h = h[..slash];

        // Bỏ port.
        var colon = h.IndexOf(':');
        if (colon >= 0) h = h[..colon];

        return h.Trim();
    }
}
