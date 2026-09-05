using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Site;

public sealed class SiteCacheSignal
{
    private CancellationTokenSource _cts = new();

    public IChangeToken Token => new CancellationChangeToken(_cts.Token);

    public void Invalidate()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}

/// <summary>
/// Tra host/slug → site. Cache nội bộ qua IMemoryCache + SiteCacheSignal:
/// khi đổi mapping domain (admin cập nhật) gọi Invalidate() để drop cache.
/// Site/SiteDomain là bảng mapping cấp hệ thống, không bị global site filter.
/// </summary>
public sealed class SiteResolver : ISiteResolver
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;
    private readonly IConfiguration _config;

    public SiteResolver(AppDbContext db, IMemoryCache cache, SiteCacheSignal signal, IConfiguration config)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _config = config;
    }

    public async Task<ResolvedSite?> ResolveAsync(string? host, CancellationToken ct = default)
    {
        var normalized = SiteHostNormalizer.Normalize(host);
        if (string.IsNullOrEmpty(normalized)) return null;

        var key = $"site:host:{normalized}";
        if (_cache.TryGetValue<ResolvedSite>(key, out var cached)) return cached;

        var site = await ResolveFromDbAsync(normalized, ct);

        // Không cache kết quả rỗng: domain vừa thêm trong admin phải có hiệu lực ngay,
        // không phải chờ Invalidate().
        if (site is null) return null;

        using var entry = _cache.CreateEntry(key);
        entry.ExpirationTokens.Add(_signal.Token);
        entry.Value = site;
        return site;
    }

    public async Task<ResolvedSite?> ResolveBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        slug = slug.Trim();

        var key = $"site:slug:{slug.ToLowerInvariant()}";
        return await _cache.GetOrCreateAsync(key, entry =>
        {
            entry.ExpirationTokens.Add(_signal.Token);
            return ResolveBySlugFromDbAsync(slug, ct);
        });
    }

    public void Invalidate() => _signal.Invalidate();

    private async Task<ResolvedSite?> ResolveFromDbAsync(string host, CancellationToken ct)
    {
        // Không lọc IsActive ở đây: site tắt vẫn phải resolve được để hiện trang bảo trì,
        // chỉ host hoàn toàn không có mapping mới trả null.

        // 1. Tra qua SiteDomain (cho phép nhiều domain/site).
        var site = await (
            from d in _db.SiteDomains.AsNoTracking()
            join s in _db.Sites.AsNoTracking() on d.SiteId equals s.Id
            where d.Host == host
            select s
        ).FirstOrDefaultAsync(ct);

        // 2. Fallback: khớp PrimaryDomain.
        site ??= await _db.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PrimaryDomain == host, ct);

        if (site is null) return null;

        return new ResolvedSite(site.Id, site.Slug, site.DefaultTheme) { IsActive = site.IsActive };
    }

    private async Task<ResolvedSite?> ResolveBySlugFromDbAsync(string slug, CancellationToken ct)
    {
        var site = await _db.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive, ct);

        return site is null ? null : new ResolvedSite(site.Id, site.Slug, site.DefaultTheme);
    }
}
