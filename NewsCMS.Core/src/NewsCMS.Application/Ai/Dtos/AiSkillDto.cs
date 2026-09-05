using NewsCMS.Domain.Entities.Ai;

namespace NewsCMS.Application.Ai.Dtos;

public record AiSkillDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    AiSkillKind Kind,
    bool IsActive,
    int SortOrder,
    // Prompt fields
    string? SystemPrompt,
    string? UserPromptTemplate,
    bool AllowStyled,
    double? Temperature,
    int? MaxTokens,
    string? Targets,
    bool UseTools,
    // Tool fields
    string? ToolType,
    string? BaseUrl,
    bool HasApiKey,
    string? ApiKeyMasked,
    string? ConfigJson);

public record AiSkillUpsertDto(
    Guid? Id,
    string Key,
    string Name,
    string? Description,
    AiSkillKind Kind,
    bool IsActive,
    int SortOrder = 0,
    // Prompt fields
    string? SystemPrompt = null,
    string? UserPromptTemplate = null,
    bool AllowStyled = false,
    double? Temperature = null,
    int? MaxTokens = null,
    string? Targets = null,
    bool UseTools = false,
    // Tool fields
    string? ToolType = null,
    string? BaseUrl = null,
    string? ApiKey = null,
    string? ConfigJson = null);
