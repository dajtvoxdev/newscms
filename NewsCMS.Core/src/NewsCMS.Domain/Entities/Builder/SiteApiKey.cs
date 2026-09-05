using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// API key cho agent/MCP thao tác site (Phase 7). SiteId = null → key phạm vi root (SuperAdmin),
/// khác null → key chỉ thao tác được site đó. Không implement ISiteScoped vì SiteId nullable và
/// việc resolve tenant do auth middleware tự làm (đọc key → set ICurrentSite).
/// Key lưu dạng mã hoá DataProtection (giải mã được để xem lại trong admin); KeyHash giữ lại
/// làm index tra cứu khi xác thực.
/// </summary>
public class SiteApiKey : BaseEntity
{
    /// <summary>null = phạm vi root; khác null = giới hạn trong một site.</summary>
    public Guid? SiteId { get; set; }

    public string Name { get; set; } = default!;
    /// <summary>SHA-256 (hex) của key gốc — index tra cứu lúc authenticate.</summary>
    public string KeyHash { get; set; } = default!;
    /// <summary>Key gốc mã hoá bằng DataProtection — admin giải mã xem lại được.</summary>
    public string? KeyCipher { get; set; }
    /// <summary>Danh sách phạm vi, CSV (ví dụ "builder.read,builder.write").</summary>
    public string Scopes { get; set; } = default!;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int RateLimitPerMinute { get; set; } = 60;
    public bool IsRevoked { get; set; }
}
