using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Sinh CSS từ SiteDesignToken: một khối gồm :root CSS variables + @theme Tailwind để cả
/// var(--color-brand-500) lẫn utility bg-brand-500 cùng trỏ một nguồn. Cache theo signal.
/// </summary>
public sealed class DesignTokenCssBuilder : IDesignTokenCssBuilder
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;
    private readonly ICurrentSite _currentSite;

    public DesignTokenCssBuilder(AppDbContext db, IMemoryCache cache, SiteCacheSignal signal, ICurrentSite currentSite)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _currentSite = currentSite;
    }

    public async Task<string> BuildCssAsync(CancellationToken ct = default)
    {
        var siteId = _currentSite.SiteId;
        var key = $"builder:tokencss:{siteId}";
        if (_cache.TryGetValue<string>(key, out var cached)) return cached;

        // SiteDesignTokens đã bị global filter scope theo site hiện tại.
        var tokens = await _db.SiteDesignTokens.AsNoTracking()
            .OrderBy(t => t.Group)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Key)
            .Select(t => new { t.Group, t.Key, t.Value })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("/* Design tokens — sinh tu SiteDesignToken (NewsCMS Site Builder). */");
        sb.AppendLine("/* Doi token o day hoac trong admin la doi toan site (ca var() lan utility). */");

        // :root CSS variables — dùng runtime cho var(--color-...).
        sb.AppendLine(":root {");
        foreach (var t in tokens)
        {
            var name = TokenName(t.Group, t.Key);
            sb.AppendLine($"  {name}: {t.Value};");
        }
        sb.AppendLine("}");

        // @theme — nguồn cho @tailwindcss/browser compile utility (bg-brand-500, rounded-card...).
        sb.AppendLine("@theme {");
        foreach (var t in tokens)
        {
            var name = TokenName(t.Group, t.Key);
            sb.AppendLine($"  {name}: {t.Value};");
        }
        sb.AppendLine("}");

        var css = sb.ToString();

        using var entry = _cache.CreateEntry(key);
        entry.ExpirationTokens.Add(_signal.Token);
        entry.Value = css;
        return css;
    }

    public async Task<string> GetCssHashAsync(CancellationToken ct = default)
    {
        var css = await BuildCssAsync(ct);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(css));
        // 12 hex đầu là đủ phân biệt cho mục đích cache-busting.
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }

    /// <summary>Tên CSS variable theo nhóm: color→--color-, font→--font-, radius→--radius-...</summary>
    private static string TokenName(DesignTokenGroup group, string key) => group switch
    {
        DesignTokenGroup.Color => $"--color-{key}",
        DesignTokenGroup.Font => $"--font-{key}",
        DesignTokenGroup.Space => $"--space-{key}",
        DesignTokenGroup.Radius => $"--radius-{key}",
        DesignTokenGroup.Shadow => $"--shadow-{key}",
        _ => $"--{key}"
    };
}
