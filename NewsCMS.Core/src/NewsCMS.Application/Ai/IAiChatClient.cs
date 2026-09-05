using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ai;

public interface IAiChatClient
{
    Task<Result<string>> CompleteAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<AiToolDefinition> tools,
        double? temperature,
        int? maxTokens,
        int timeoutSeconds,
        CancellationToken ct = default);

    Task<Result<string>> CompleteStreamingAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<AiToolDefinition> tools,
        Func<string, CancellationToken, Task> onDelta,
        double? temperature,
        int? maxTokens,
        int timeoutSeconds,
        CancellationToken ct = default);
}

public record AiToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema,
    Func<System.Text.Json.JsonElement, CancellationToken, Task<string>> Invoke);
