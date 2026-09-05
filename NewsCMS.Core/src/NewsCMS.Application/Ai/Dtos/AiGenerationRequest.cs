using NewsCMS.Domain.Entities.Ai;

namespace NewsCMS.Application.Ai.Dtos;

public record AiGenerationRequest(
    string SkillKey,
    AiGenerationStyle Style,
    string? Title,
    string? Content,
    string? Selection,
    string? Language,
    Guid? ConnectionId);

public record AiGenerationResult(string Content);
