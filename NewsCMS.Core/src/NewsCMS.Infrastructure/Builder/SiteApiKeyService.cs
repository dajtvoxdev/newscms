using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Quản lý + xác thực API key cho agent/MCP. Key gốc được lưu MÃ HOÁ (DataProtection) để admin
/// xem lại được; KeyHash (SHA-256) làm index tra cứu lúc authenticate. SiteApiKey không phải
/// ISiteScoped (SiteId nullable = phạm vi root) nên mọi truy vấn ở đây chạy với BypassSiteScope
/// và tự lọc theo siteId tường minh.
/// </summary>
public sealed class SiteApiKeyService : ISiteApiKeyService
{
    /// <summary>Tiền tố để nhận diện key trong log/UI mà không lộ phần bí mật.</summary>
    private const string KeyPrefix = "ncsk_";

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;

    public SiteApiKeyService(AppDbContext db, IDataProtectionProvider dp)
    {
        _db = db;
        _protector = dp.CreateProtector("NewsCMS.Builder.SiteApiKey");
    }

    public async Task<IReadOnlyList<SiteApiKeyDto>> ListAsync(
        Guid? siteId, string? status = null, CancellationToken ct = default)
    {
        var previous = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            var query = _db.SiteApiKeys.AsNoTracking().AsQueryable();

            // siteId null = gọi từ root → xem tất cả. Khác null = chỉ key của site đó.
            if (siteId is { } id) query = query.Where(k => k.SiteId == id);

            // Lọc trạng thái đẩy xuống SQL; mặc định "active" = còn lưu hành (chưa thu hồi, chưa hết hạn).
            var now = DateTime.UtcNow;
            switch (status)
            {
                case ApiKeyStatusFilters.Revoked:
                    query = query.Where(k => k.IsRevoked);
                    break;
                case ApiKeyStatusFilters.Expired:
                    query = query.Where(k => !k.IsRevoked && k.ExpiresAt != null && k.ExpiresAt <= now);
                    break;
                case ApiKeyStatusFilters.All:
                    break;
                default: // active — kể cả khi caller không truyền gì
                    query = query.Where(k => !k.IsRevoked && (k.ExpiresAt == null || k.ExpiresAt > now));
                    break;
            }

            return await query
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new SiteApiKeyDto(
                    k.Id, k.SiteId, k.Name, k.Scopes, k.ExpiresAt, k.LastUsedAt,
                    k.RateLimitPerMinute, k.IsRevoked, k.CreatedAt))
                .ToListAsync(ct);
        }
        finally
        {
            _db.BypassSiteScope = previous;
        }
    }

    public async Task<Result<SiteApiKeyCreatedDto>> CreateAsync(
        SiteApiKeyCreateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SiteApiKeyCreatedDto>.Failure("Tên key không được để trống.");

        var scopes = NormalizeScopes(request.Scopes);
        if (scopes.Count == 0)
            return Result<SiteApiKeyCreatedDto>.Failure(
                $"Scope không hợp lệ. Hợp lệ: {string.Join(", ", ApiKeyScopes.All)}.");

        var previous = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            if (request.SiteId is { } siteId && !await _db.Sites.AnyAsync(s => s.Id == siteId, ct))
                return Result<SiteApiKeyCreatedDto>.Failure("Site không tồn tại.");

            var plainKey = GenerateKey();

            var entity = new SiteApiKey
            {
                SiteId = request.SiteId,
                Name = request.Name.Trim(),
                KeyHash = Hash(plainKey),
                KeyCipher = _protector.Protect(plainKey),
                Scopes = string.Join(",", scopes),
                ExpiresAt = request.ExpiresAt,
                RateLimitPerMinute = request.RateLimitPerMinute <= 0 ? 60 : request.RateLimitPerMinute,
                IsRevoked = false
            };

            _db.SiteApiKeys.Add(entity);
            await _db.SaveChangesAsync(ct);

            var dto = new SiteApiKeyDto(
                entity.Id, entity.SiteId, entity.Name, entity.Scopes, entity.ExpiresAt,
                entity.LastUsedAt, entity.RateLimitPerMinute, entity.IsRevoked, entity.CreatedAt);

            return Result<SiteApiKeyCreatedDto>.Success(new SiteApiKeyCreatedDto(dto, plainKey));
        }
        finally
        {
            _db.BypassSiteScope = previous;
        }
    }

    public async Task<Result> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var previous = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            var key = await _db.SiteApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
            if (key is null) return Result.Failure("Key không tồn tại.");

            key.IsRevoked = true;
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        finally
        {
            _db.BypassSiteScope = previous;
        }
    }

    public async Task<string?> RevealAsync(Guid id, CancellationToken ct = default)
    {
        var previous = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            var key = await _db.SiteApiKeys.AsNoTracking()
                .FirstOrDefaultAsync(k => k.Id == id, ct);
            if (key?.KeyCipher is null) return null;

            try
            {
                return _protector.Unprotect(key.KeyCipher);
            }
            catch (CryptographicException)
            {
                // Key ring đổi/thiếu (vd. di chuyển server) — không quăng lỗi ra UI.
                return null;
            }
        }
        finally
        {
            _db.BypassSiteScope = previous;
        }
    }

    public async Task<AuthenticatedApiKey?> AuthenticateAsync(
        string plainKey, string? requestedSiteSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainKey)) return null;

        var hash = Hash(plainKey.Trim());

        var previous = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            var key = await _db.SiteApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
            if (key is null || key.IsRevoked) return null;
            if (key.ExpiresAt is { } exp && exp <= DateTime.UtcNow) return null;

            // Key gắn site: BỎ QUA requestedSiteSlug hoàn toàn — đây là chốt chặn không cho key
            // của site A thao tác lên site B dù header có khai gì.
            var site = key.SiteId is { } boundSiteId
                ? await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == boundSiteId, ct)
                : string.IsNullOrWhiteSpace(requestedSiteSlug)
                    ? null
                    : await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == requestedSiteSlug, ct);

            // Key root không khai site → không biết thao tác lên đâu, từ chối.
            if (site is null || !site.IsActive) return null;

            key.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new AuthenticatedApiKey(
                KeyId: key.Id,
                SiteId: site.Id,
                SiteSlug: site.Slug,
                SiteTheme: site.DefaultTheme,
                Scopes: NormalizeScopes(key.Scopes),
                RateLimitPerMinute: key.RateLimitPerMinute <= 0 ? 60 : key.RateLimitPerMinute);
        }
        finally
        {
            _db.BypassSiteScope = previous;
        }
    }

    /// <summary>Sinh key ngẫu nhiên 32 byte, mã base64url để dán được vào header/URL.</summary>
    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return KeyPrefix + body;
    }

    private static string Hash(string plainKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Lọc bỏ scope lạ — key chỉ mang được scope nằm trong danh sách hợp lệ.</summary>
    private static List<string> NormalizeScopes(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => ApiKeyScopes.All.Contains(s))
            .Distinct()
            .ToList();
}
