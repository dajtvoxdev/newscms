using NewsCMS.Application.Common;
using NewsCMS.Application.Engagement.Dtos;

namespace NewsCMS.Application.Engagement;

public interface ICommitmentService
{
    Task<Result<Guid>> CreateAsync(
        CommitmentCreateDto dto,
        string signerKey,
        string ipHash,
        string userAgent,
        CancellationToken ct = default);

    Task<bool> HasSignedAsync(string signerKey, string ipHash, CancellationToken ct = default);

    Task<IReadOnlyList<CommitmentWallItemDto>> GetWallAsync(int take = 200, CancellationToken ct = default);

    Task<IReadOnlyList<CommitmentWallItemDto>> GetWallSinceAsync(DateTime sinceUtc, CancellationToken ct = default);

    Task<PagedList<CommitmentAdminItemDto>> GetForAdminAsync(string? keyword, int page, int pageSize, CancellationToken ct = default);

    Task<Result> ToggleHiddenAsync(Guid id, CancellationToken ct = default);

    Task<int> GetTotalCountAsync(CancellationToken ct = default);
}
