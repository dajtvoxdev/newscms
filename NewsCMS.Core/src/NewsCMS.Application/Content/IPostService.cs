using NewsCMS.Application.Common;
using NewsCMS.Application.Content.Dtos;

namespace NewsCMS.Application.Content;

public interface IPostService
{
    Task<PagedList<PostListItemDto>> SearchAsync(
        string? keyword, Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<PostDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PostDto?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<PostListItemDto>> GetFeaturedAsync(int take = 6, CancellationToken ct = default);
    Task<IReadOnlyList<PostListItemDto>> GetMostReadAsync(int take = 10, CancellationToken ct = default);
    Task<IReadOnlyList<PostListItemDto>> GetByCategoryAsync(string categorySlug, int take = 10, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(PostCreateDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(PostUpdateDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> PublishAsync(Guid id, CancellationToken ct = default);

    Task IncreaseViewAsync(Guid id, CancellationToken ct = default);
}
