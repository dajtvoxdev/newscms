using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ai;

public interface IAiConnectionService
{
    Task<IReadOnlyList<AiConnectionDto>> GetAllAsync(CancellationToken ct = default);
    Task<AiConnectionDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> CreateAsync(AiConnectionUpsertDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(AiConnectionUpsertDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> SetDefaultAsync(Guid id, CancellationToken ct = default);
    Task<Result> TestAsync(Guid id, CancellationToken ct = default);
}
