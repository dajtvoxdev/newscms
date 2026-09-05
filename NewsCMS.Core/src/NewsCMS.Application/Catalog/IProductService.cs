using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Catalog;

public interface IProductService
{
    Task<PagedList<ProductListItemDto>> SearchAsync(
        string? keyword, Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<PagedList<ProductListItemDto>> SearchPublishedAsync(
        Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<ProductDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductDetailDto?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<ProductListItemDto>> GetFeaturedAsync(int take = 8, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItemDto>> GetByCategoryAsync(string categorySlug, int take = 20, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(ProductUpsertDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> TogglePublishAsync(Guid id, CancellationToken ct = default);
    Task IncrementViewAsync(Guid id, CancellationToken ct = default);
}
