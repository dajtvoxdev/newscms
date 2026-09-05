using NewsCMS.Application.Ar.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ar;

public interface IArExperienceService
{
    Task<PagedList<ArExperienceDto>> SearchAsync(
        string? keyword, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<ArExperienceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ArExperienceDetailDto?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<string?> GetFirstPublishedSlugAsync(CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ArExperienceUpsertDto dto, CancellationToken ct = default);

    Task<Result> UpdateAsync(ArExperienceUpsertDto dto, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result> TogglePublishAsync(Guid id, CancellationToken ct = default);

    Task IncrementViewAsync(Guid id, CancellationToken ct = default);
}
