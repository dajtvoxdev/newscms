using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho Category mở rộng (Phase 5) ─────────────────────────────────────────

public sealed record BuilderCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string Type,
    string? PathSlug,
    Guid? TemplatePageId,
    Guid? LayoutId,
    Guid? CoverImageId,
    string? Color,
    string? Icon,
    int Order,
    bool IsActive);

public sealed record BuilderCategorySaveRequest(
    string Name,
    string Slug,
    string? Description,
    string Type,
    Guid? TemplatePageId,
    Guid? LayoutId,
    Guid? CoverImageId,
    string? Color,
    string? Icon,
    int Order,
    bool IsActive);

/// <summary>
/// CRUD chuyên mục mở rộng cho builder: thêm type, pathSlug, templatePageId, layoutId, coverImage.
/// Khi slug đổi → tự tạo redirect 301 từ path cũ (qua IRouteRegistry).
/// </summary>
public interface IBuilderCategoryService
{
    Task<Result<BuilderCategoryDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedList<BuilderCategoryDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<Result<BuilderCategoryDto>> CreateAsync(BuilderCategorySaveRequest request, CancellationToken ct = default);
    Task<Result<BuilderCategoryDto>> UpdateAsync(Guid id, BuilderCategorySaveRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
