using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho SEO meta (Phase 5) ─────────────────────────────────────────────────

public sealed record SeoMetaDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Culture,
    string? MetaTitle,
    string? MetaDescription,
    string? OgImage,
    string? Canonical,
    string? Robots,
    string? SchemaJsonLd,
    string? OgType,
    string? TwitterCard,
    double? Priority,
    string? ChangeFreq);

public sealed record SeoMetaSaveRequest(
    string EntityType,
    Guid EntityId,
    string Culture,
    string? MetaTitle,
    string? MetaDescription,
    string? OgImage,
    string? Canonical,
    string? Robots,
    string? SchemaJsonLd,
    string? OgType,
    string? TwitterCard,
    double? Priority,
    string? ChangeFreq);

/// <summary>
/// CRUD SEO meta cho bất kỳ entity nào (Page/Category/Post/Product). Một entity có thể có
/// nhiều bản SEO theo culture. JSON-LD emitter hỗ trợ Article, BreadcrumbList, WebSite...
/// </summary>
public interface ISeoMetaService
{
    Task<SeoMetaDto?> GetAsync(string entityType, Guid entityId, string culture, CancellationToken ct = default);
    Task<Result<SeoMetaDto>> SaveAsync(SeoMetaSaveRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sinh JSON-LD structured data cho một entity.</summary>
    Task<string?> GenerateJsonLdAsync(string entityType, Guid entityId, string culture, CancellationToken ct = default);
}
