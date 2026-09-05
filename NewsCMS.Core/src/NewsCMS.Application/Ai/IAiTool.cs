using System.Text.Json;

namespace NewsCMS.Application.Ai;

public interface IAiTool
{
    string Key { get; }
    string DisplayName { get; }
    string Description { get; }
    string ParametersJsonSchema { get; }
    Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default);
}

public record AiToolContext(
    string? ApiKey,
    string? BaseUrl,
    string? ConfigJson);
