using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ai;

public interface IAiSkillService
{
    Task<IReadOnlyList<AiSkillDto>> GetAllAsync(CancellationToken ct = default);
    Task<AiSkillDto?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<AiSkillDto>> GetPromptSkillsForTargetAsync(string target, CancellationToken ct = default);
    Task<IReadOnlyList<AiSkillDto>> GetActiveToolSkillsAsync(CancellationToken ct = default);
    Task<Result<Guid>> CreateAsync(AiSkillUpsertDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(AiSkillUpsertDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
