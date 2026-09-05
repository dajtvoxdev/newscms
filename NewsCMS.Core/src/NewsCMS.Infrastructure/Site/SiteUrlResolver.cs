using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Site;

/// <summary>
/// Tra base URL chính thức của site (xem <see cref="ISiteUrlResolver"/>).
///
/// Thứ tự nguồn, và lý do của thứ tự đó:
/// <list type="number">
///   <item><c>Site.PrimaryDomain</c> — trường admin khai tay, ưu tiên cao nhất;</item>
///   <item><c>SiteDomains</c> có <c>IsPrimary</c> — nguồn thật khi PrimaryDomain bỏ trống
///         (chu-kafe đang đúng trường hợp này: <c>tourimate.site</c> nằm ở đây);</item>
///   <item>host của request hiện tại — chỉ cho site dev chưa khai domain nào.</item>
/// </list>
///
/// Host request KHÔNG được đứng trước hai nguồn kia: một site nghe nhiều domain (chu-kafe có cả
/// <c>tourimate.site</c> lẫn <c>localhost</c>), lấy theo host thì mỗi domain tự khai mình là
/// canonical và mất đúng tác dụng hợp nhất URL trùng nội dung của thẻ này.
///
/// Cache theo pattern <c>SiteResolver</c> (IMemoryCache + <see cref="SiteCacheSignal"/>): domain
/// gần như không đổi, nhưng đổi thì phải thấy ngay.
/// </summary>
public sealed class SiteUrlResolver : ISiteUrlResolver
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SiteUrlResolver(
        AppDbContext db,
        ICurrentSite currentSite,
        IMemoryCache cache,
        SiteCacheSignal signal,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _currentSite = currentSite;
        _cache = cache;
        _signal = signal;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> GetBaseUrlAsync(CancellationToken ct = default)
    {
        var siteId = _currentSite.SiteId;
        var key = $"siteurl:{siteId}";

        // Chỉ cache phần tra DB. Host request thuộc về từng request nên không được nằm trong cache
        // — nhớ nó lại là mọi request sau đó nhận domain của request đầu tiên.
        if (!_cache.TryGetValue<string?>(key, out var domain))
        {
            domain = await ResolveDomainFromDbAsync(siteId, ct);

            using var entry = _cache.CreateEntry(key);
            entry.ExpirationTokens.Add(_signal.Token);
            entry.AbsoluteExpirationRelativeToNow =
                domain is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(30);
            entry.Value = domain;
        }

        if (!string.IsNullOrWhiteSpace(domain))
            return $"https://{domain}";

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null && request.Host.HasValue)
            return $"{request.Scheme}://{request.Host.Value}";

        return string.Empty;
    }

    public async Task<string?> ToAbsoluteAsync(string? pathOrUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return pathOrUrl;

        var value = pathOrUrl.Trim();
        if (IsAlreadyAbsolute(value)) return value;

        var baseUrl = await GetBaseUrlAsync(ct);
        return Combine(baseUrl, value);
    }

    /// <summary>Giá trị đã là URL đầy đủ hoặc protocol-relative thì không được ghép domain nữa.</summary>
    public static bool IsAlreadyAbsolute(string value) =>
        value.StartsWith("//", StringComparison.Ordinal)
        || value.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// Ghép base URL với path. Không có base URL thì trả path nguyên vẹn — thẻ tương đối vẫn hơn
    /// một URL hỏng dạng <c>https:///path</c>.
    /// </summary>
    public static string Combine(string baseUrl, string pathOrUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return pathOrUrl;
        return pathOrUrl.StartsWith('/') ? baseUrl + pathOrUrl : $"{baseUrl}/{pathOrUrl}";
    }

    /// <summary>
    /// Domain thuần (không scheme, không "/" cuối) từ DB, hoặc null nếu site chưa khai domain nào.
    /// </summary>
    private async Task<string?> ResolveDomainFromDbAsync(Guid siteId, CancellationToken ct)
    {
        var primary = await _db.Sites.AsNoTracking()
            .Where(s => s.Id == siteId)
            .Select(s => s.PrimaryDomain)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(primary))
            return Normalize(primary);

        // SiteDomains không bị global query filter (bảng mapping cấp hệ thống) nên phải tự lọc
        // theo SiteId — thiếu vế này là site nào cũng nhận domain của bản ghi đầu bảng.
        var fromDomains = await _db.SiteDomains.AsNoTracking()
            .Where(d => d.SiteId == siteId)
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.Host)
            .Select(d => d.Host)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(fromDomains) ? null : Normalize(fromDomains);
    }

    /// <summary>Bỏ scheme nếu ai đó lỡ lưu cả "https://", bỏ "/" cuối.</summary>
    private static string Normalize(string domain)
    {
        var d = domain.Trim();
        var schemeIndex = d.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0) d = d[(schemeIndex + 3)..];
        return d.TrimEnd('/');
    }
}
