using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho API key của agent/MCP (Phase 7) ────────────────────────────────────

public sealed record SiteApiKeyDto(
    Guid Id,
    Guid? SiteId,
    string Name,
    string Scopes,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    int RateLimitPerMinute,
    bool IsRevoked,
    DateTime CreatedAt);

/// <summary>Kết quả tạo key — PlainKey cũng được lưu mã hoá để xem lại trong admin.</summary>
public sealed record SiteApiKeyCreatedDto(SiteApiKeyDto Key, string PlainKey);

public sealed record SiteApiKeyCreateRequest(
    Guid? SiteId,
    string Name,
    string Scopes,
    DateTime? ExpiresAt,
    int RateLimitPerMinute);

/// <summary>Key đã xác thực — middleware dùng để set ICurrentSite và kiểm tra scope.</summary>
public sealed record AuthenticatedApiKey(
    Guid KeyId,
    Guid SiteId,
    string SiteSlug,
    string SiteTheme,
    IReadOnlyList<string> Scopes,
    int RateLimitPerMinute);

/// <summary>
/// Quản lý và xác thực API key cho agent/MCP. Key lưu mã hoá (DataProtection) nên admin
/// xem lại được; KeyHash là index tra cứu khi xác thực.
/// Key có SiteId = null là phạm vi root — vẫn phải chỉ định site khi gọi (qua header site slug).
/// </summary>
public interface ISiteApiKeyService
{
    /// <summary>Danh sách key. Root thấy tất cả; admin site chỉ thấy key của site mình.
    /// <paramref name="status"/> lọc theo trạng thái — null lấy tất cả, giá trị hợp lệ xem <see cref="ApiKeyStatusFilters"/>.</summary>
    Task<IReadOnlyList<SiteApiKeyDto>> ListAsync(Guid? siteId, string? status = null, CancellationToken ct = default);

    /// <summary>Tạo key mới. Key thô cũng được lưu mã hoá để admin xem lại sau.</summary>
    Task<Result<SiteApiKeyCreatedDto>> CreateAsync(SiteApiKeyCreateRequest request, CancellationToken ct = default);

    /// <summary>Thu hồi key (IsRevoked = true). Không xoá để giữ dấu vết audit.</summary>
    Task<Result> RevokeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Giải mã key thô để hiển thị lại trong admin. Trả null nếu key không tồn tại/không giải mã được.</summary>
    Task<string?> RevealAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Xác thực key thô: kiểm hash, hạn dùng, trạng thái thu hồi, và resolve site đích.
    /// <paramref name="requestedSiteSlug"/> chỉ dùng cho key phạm vi root (SiteId = null);
    /// key gắn site bỏ qua tham số này để không thể trỏ sang site khác.
    /// Trả null nếu key không hợp lệ — caller trả 401, không tiết lộ lý do cụ thể.
    /// </summary>
    Task<AuthenticatedApiKey?> AuthenticateAsync(string plainKey, string? requestedSiteSlug, CancellationToken ct = default);
}

/// <summary>Các scope hợp lệ của API key.</summary>
public static class ApiKeyScopes
{
    /// <summary>Đọc schema, export, list, preview.</summary>
    public const string BuilderRead = "builder.read";
    /// <summary>Apply, tạo/sửa page, layout, category, token, menu, setting.</summary>
    public const string BuilderWrite = "builder.write";
    /// <summary>Sửa custom JS/CSS — tách riêng vì tương đương thực thi mã trên trình duyệt khách.</summary>
    public const string BuilderCode = "builder.code";

    public static readonly IReadOnlyList<string> All = new[] { BuilderRead, BuilderWrite, BuilderCode };
}

/// <summary>Bộ lọc trạng thái của trang danh sách API key (query string ?status=).</summary>
public static class ApiKeyStatusFilters
{
    /// <summary>Mặc định: còn lưu hành — chưa thu hồi và chưa hết hạn.</summary>
    public const string Active = "active";
    /// <summary>Đã thu hồi.</summary>
    public const string Revoked = "revoked";
    /// <summary>Quá hạn dùng nhưng chưa bị thu hồi.</summary>
    public const string Expired = "expired";
    /// <summary>Tất cả trạng thái.</summary>
    public const string All = "all";

    public static bool IsValid(string? value) =>
        value is null || value is Active or Revoked or Expired or All;
}
