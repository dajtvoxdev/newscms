namespace NewsCMS.Application.Ai.Dtos;

public record AiConnectionDto(
    Guid Id,
    string Name,
    string Provider,
    string BaseUrl,
    string DefaultModel,
    bool IsActive,
    bool IsDefault,
    int TimeoutSeconds,
    string? Description,
    bool HasApiKey,
    string? ApiKeyMasked);

public record AiConnectionUpsertDto(
    Guid? Id,
    string Name,
    string Provider,
    string BaseUrl,
    string? ApiKey,
    string DefaultModel,
    bool IsActive,
    bool IsDefault,
    int TimeoutSeconds = 60,
    string? Description = null);
