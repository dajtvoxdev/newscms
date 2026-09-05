namespace NewsCMS.Application.Site;

/// <summary>
/// Site hiện tại của request (scoped). Middleware resolve theo host rồi gọi Set().
/// AppDbContext đọc SiteId để áp global query filter + stamp khi thêm entity mới.
/// </summary>
public interface ICurrentSite
{
    Guid SiteId { get; }
    string Slug { get; }
    string Theme { get; }
    bool IsResolved { get; }
    void Set(Guid siteId, string slug, string theme);
}

/// <summary>Tra host → site. Có cache nội bộ; gọi Invalidate() khi đổi mapping domain.</summary>
public interface ISiteResolver
{
    /// <summary>Trả null khi host không map tới site nào (caller quyết định 404).</summary>
    Task<ResolvedSite?> ResolveAsync(string? host, CancellationToken ct = default);
    Task<ResolvedSite?> ResolveBySlugAsync(string slug, CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// Site đã resolve. IsActive = false vẫn trả về (không phải "không tìm thấy") để
/// MaintenanceMiddleware phân biệt được "site tạm tắt" với "host chưa cấu hình".
/// </summary>
public record ResolvedSite(Guid SiteId, string Slug, string Theme)
{
    public bool IsActive { get; init; } = true;
}
