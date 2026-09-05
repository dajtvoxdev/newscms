using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho Translation (Phase 5) ──────────────────────────────────────────────

public sealed record PageTranslationDto(
    Guid Id,
    Guid PageId,
    string Culture,
    string Title,
    string Slug,
    string? BuilderJson,
    string? CompiledHtml);

public sealed record PageTranslationSaveRequest(
    Guid PageId,
    string Culture,
    string Title,
    string Slug,
    string? BuilderJson,
    string? CompiledHtml,
    string? CompiledCss,
    string? CustomJs);

/// <summary>
/// CRUD bản dịch trang theo culture. Renderer chọn bản dịch khớp Culture của request,
/// fallback về entity gốc nếu chưa có. hreflang sinh từ tập SiteRoute cùng TargetId khác Culture.
/// </summary>
public interface IPageTranslationService
{
    Task<IReadOnlyList<PageTranslationDto>> ListByPageAsync(Guid pageId, CancellationToken ct = default);
    Task<PageTranslationDto?> GetAsync(Guid pageId, string culture, CancellationToken ct = default);
    Task<Result<PageTranslationDto>> SaveAsync(PageTranslationSaveRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
