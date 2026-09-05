using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ai;

public interface IAiCompletionService
{
    Task<Result<AiGenerationResult>> GenerateAsync(AiGenerationRequest request, CancellationToken ct = default);
}
