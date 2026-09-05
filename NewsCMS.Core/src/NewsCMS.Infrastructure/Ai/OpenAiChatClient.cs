using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Common;

namespace NewsCMS.Infrastructure.Ai;

public sealed class OpenAiChatClient : IAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<OpenAiChatClient> _logger;

    public OpenAiChatClient(ILogger<OpenAiChatClient> logger)
    {
        _logger = logger;
    }

    public async Task<Result<string>> CompleteAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<AiToolDefinition> tools,
        double? temperature,
        int? maxTokens,
        int timeoutSeconds,
        CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var endpoint = $"{baseUrl.TrimEnd('/')}/chat/completions";
            var chatMessages = new JsonArray();
            foreach (var message in messages)
            {
                chatMessages.Add(new JsonObject
                {
                    ["role"] = NormalizeRole(message.Role),
                    ["content"] = message.Content
                });
            }

            var toolMap = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var toolPayload = BuildTools(tools);

            const int maxRounds = 4;
            for (var round = 0; round < maxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                var payload = BuildPayload(model, chatMessages, toolPayload, temperature, maxTokens, round == 0 ? "required" : "auto");
                var response = await PostAsync(client, endpoint, payload, ct);
                if (!response.Succeeded)
                    return Result<string>.Failure(response.Error!);

                var completion = response.Value!;
                var choice = completion.RootElement.GetProperty("choices")[0];
                var finishReason = choice.TryGetProperty("finish_reason", out var reason)
                    ? reason.GetString()
                    : null;
                var responseMessage = choice.GetProperty("message");

                if (!string.Equals(finishReason, "tool_calls", StringComparison.OrdinalIgnoreCase))
                    return ExtractTextResult(responseMessage, finishReason);

                var toolCalls = responseMessage.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array
                    ? calls
                    : default;
                if (toolCalls.ValueKind != JsonValueKind.Array)
                    return Result<string>.Failure("AI yêu cầu tool nhưng không trả về tool_calls hợp lệ.");

                chatMessages.Add(CloneMessage(responseMessage));

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var toolCallId = toolCall.GetProperty("id").GetString() ?? "";
                    var function = toolCall.GetProperty("function");
                    var functionName = function.GetProperty("name").GetString() ?? "";
                    var arguments = function.TryGetProperty("arguments", out var args)
                        ? args.GetString() ?? "{}"
                        : "{}";

                    var toolResult = await ExecuteToolAsync(toolMap, functionName, arguments, ct);
                    chatMessages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolCallId,
                        ["content"] = toolResult
                    });
                }
            }

            chatMessages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Bạn đã có đủ dữ liệu từ các tool ở trên. Không gọi thêm tool. Hãy trả lời ngay bây giờ theo đúng định dạng mà người dùng yêu cầu, thật ngắn gọn và không dùng markdown."
            });

            var finalPayload = BuildPayload(model, chatMessages, null, temperature, maxTokens);
            var finalResponse = await PostAsync(client, endpoint, finalPayload, ct);
            if (!finalResponse.Succeeded)
                return Result<string>.Failure(finalResponse.Error!);

            var finalChoice = finalResponse.Value!.RootElement.GetProperty("choices")[0];
            var finalFinishReason = finalChoice.TryGetProperty("finish_reason", out var finalReason)
                ? finalReason.GetString()
                : null;
            return ExtractTextResult(finalChoice.GetProperty("message"), finalFinishReason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Yêu cầu đã hết thời gian chờ.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat error (baseUrl={BaseUrl}, model={Model})", baseUrl, model);
            return Result<string>.Failure("Lỗi khi gọi AI. Vui lòng thử lại.");
        }
    }

    public async Task<Result<string>> CompleteStreamingAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<AiToolDefinition> tools,
        Func<string, CancellationToken, Task> onDelta,
        double? temperature,
        int? maxTokens,
        int timeoutSeconds,
        CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var endpoint = $"{baseUrl.TrimEnd('/')}/chat/completions";
            var chatMessages = new JsonArray();
            foreach (var message in messages)
            {
                chatMessages.Add(new JsonObject
                {
                    ["role"] = NormalizeRole(message.Role),
                    ["content"] = message.Content
                });
            }

            var toolMap = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var toolPayload = BuildTools(tools);
            var fullText = new System.Text.StringBuilder();

            const int maxRounds = 4;
            for (var round = 0; round < maxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                var payload = BuildPayload(model, chatMessages, toolPayload, temperature, maxTokens, round == 0 ? "required" : "auto");
                payload["stream"] = true;
                var streamResult = await StreamRoundAsync(client, endpoint, payload, toolMap, onDelta, fullText, ct);
                if (!streamResult.Succeeded)
                    return Result<string>.Failure(streamResult.Error!);

                var roundResult = streamResult.Value!;
                if (roundResult.ToolCalls.Count == 0)
                    return string.IsNullOrWhiteSpace(fullText.ToString())
                        ? Result<string>.Failure("AI không trả về nội dung.")
                        : Result<string>.Success(fullText.ToString());

                chatMessages.Add(roundResult.AssistantMessage);
                foreach (var call in roundResult.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(toolMap, call.Name, call.Arguments, ct);
                    chatMessages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = call.Id,
                        ["content"] = toolResult
                    });
                }
            }

            chatMessages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Bạn đã có đủ dữ liệu từ các tool ở trên. Không gọi thêm tool. Hãy trả lời ngay bây giờ theo đúng định dạng mà người dùng yêu cầu, thật ngắn gọn và không dùng markdown."
            });

            var finalPayload = BuildPayload(model, chatMessages, null, temperature, maxTokens);
            finalPayload["stream"] = true;
            var finalResult = await StreamRoundAsync(client, endpoint, finalPayload, toolMap, onDelta, fullText, ct);
            if (!finalResult.Succeeded)
                return Result<string>.Failure(finalResult.Error!);

            return string.IsNullOrWhiteSpace(fullText.ToString())
                ? Result<string>.Failure("AI không trả về nội dung.")
                : Result<string>.Success(fullText.ToString());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Yêu cầu đã hết thời gian chờ.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI streaming chat error (baseUrl={BaseUrl}, model={Model})", baseUrl, model);
            return Result<string>.Failure("Lỗi khi gọi AI. Vui lòng thử lại.");
        }
    }

    private static async Task<Result<StreamRoundResult>> StreamRoundAsync(
        HttpClient client,
        string endpoint,
        JsonObject payload,
        IReadOnlyDictionary<string, AiToolDefinition> toolMap,
        Func<string, CancellationToken, Task> onDelta,
        System.Text.StringBuilder fullText,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return Result<StreamRoundResult>.Failure(TryReadProviderError(errorBody));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var toolCalls = new SortedDictionary<int, ToolCallBuffer>();
        var pendingText = new System.Text.StringBuilder();
        string? finishReason = null;
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var choice = doc.RootElement.GetProperty("choices")[0];
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                finishReason = reason.GetString();
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var piece = content.GetString() ?? string.Empty;
                if (piece.Length > 0)
                {
                    pendingText.Append(piece);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var idx) && idx.TryGetInt32(out var parsed) ? parsed : toolCalls.Count;
                    if (!toolCalls.TryGetValue(index, out var buffer))
                    {
                        buffer = new ToolCallBuffer();
                        toolCalls[index] = buffer;
                    }

                    if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        buffer.Id = id.GetString() ?? buffer.Id;
                    if (call.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                            buffer.Name = (buffer.Name ?? string.Empty) + (name.GetString() ?? string.Empty);
                        if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                            buffer.Arguments.Append(args.GetString());
                    }
                }
            }
        }

        if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            return Result<StreamRoundResult>.Failure("Nội dung bị chặn bởi bộ lọc an toàn của AI provider.");

        var completedCalls = toolCalls.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new CompletedToolCall(x.Id, x.Name!, x.Arguments.ToString()))
            .ToList();
        if (completedCalls.Count == 0 && pendingText.Length > 0)
        {
            var text = pendingText.ToString();
            fullText.Append(text);
            await onDelta(text, ct);
        }

        var assistant = new JsonObject { ["role"] = "assistant" };
        if (completedCalls.Count > 0)
        {
            var callArray = new JsonArray();
            foreach (var call in completedCalls)
            {
                callArray.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments
                    }
                });
            }
            assistant["tool_calls"] = callArray;
        }
        else
        {
            assistant["content"] = pendingText.ToString();
        }

        return Result<StreamRoundResult>.Success(new StreamRoundResult(assistant, completedCalls));
    }

    private static string NormalizeRole(string role) =>
        role.ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user"
        };

    private static JsonArray? BuildTools(IReadOnlyList<AiToolDefinition> tools)
    {
        if (tools.Count == 0)
            return null;

        var result = new JsonArray();
        foreach (var tool in tools)
        {
            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema)
                }
            });
        }

        return result;
    }

    private static JsonObject BuildPayload(
        string model,
        JsonArray messages,
        JsonArray? tools,
        double? temperature,
        int? maxTokens,
        string toolChoice = "auto")
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone()
        };

        if (temperature.HasValue)
            payload["temperature"] = temperature.Value;
        if (maxTokens.HasValue)
            payload["max_tokens"] = maxTokens.Value;
        if (tools is not null && tools.Count > 0)
        {
            payload["tools"] = tools.DeepClone();
            payload["tool_choice"] = toolChoice;
        }

        return payload;
    }

    private static JsonObject CloneMessage(JsonElement message)
    {
        var result = new JsonObject
        {
            ["role"] = message.GetProperty("role").GetString() ?? "assistant"
        };

        if (message.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
            result["content"] = JsonNode.Parse(content.GetRawText());

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            result["tool_calls"] = JsonNode.Parse(toolCalls.GetRawText());

        return result;
    }

    private async Task<string> ExecuteToolAsync(
        IReadOnlyDictionary<string, AiToolDefinition> tools,
        string functionName,
        string arguments,
        CancellationToken ct)
    {
        if (!tools.TryGetValue(functionName, out var tool))
        {
            _logger.LogWarning("AI tool CALL {Name} không tồn tại trong toolMap. args={Args}", functionName, arguments);
            return $"Tool '{functionName}' không tìm thấy.";
        }

        _logger.LogInformation("AI tool CALL {Name} args={Args}", functionName, arguments);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            var result = await tool.Invoke(doc.RootElement, ct);
            _logger.LogInformation("AI tool RESULT {Name} => {Result}",
                functionName, result.Length > 400 ? result[..400] + "…(" + result.Length + " chars)" : result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI tool ERROR {Name} args={Args}", functionName, arguments);
            return $"Lỗi chạy tool: {ex.Message}";
        }
    }

    private static Result<string> ExtractTextResult(JsonElement message, string? finishReason)
    {
        if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure("Nội dung bị chặn bởi bộ lọc an toàn của AI provider.");

        if (!message.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
            return Result<string>.Failure("AI không trả về nội dung.");

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString() ?? "";
            return string.IsNullOrWhiteSpace(text) && string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
                ? Result<string>.Failure("AI dừng vì giới hạn output nhưng không trả về nội dung.")
                : Result<string>.Success(text);
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var pieces = content.EnumerateArray()
                .Select(x => x.TryGetProperty("text", out var text) ? text.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x));
            return Result<string>.Success(string.Join("", pieces));
        }

        return Result<string>.Failure("AI trả về định dạng nội dung không hỗ trợ.");
    }

    private static async Task<Result<JsonDocument>> PostAsync(
        HttpClient client,
        string endpoint,
        JsonObject payload,
        CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(endpoint, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var error = TryReadProviderError(errorBody);

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => Result<JsonDocument>.Failure("API key không hợp lệ."),
                (System.Net.HttpStatusCode)429 => Result<JsonDocument>.Failure("Đã vượt giới hạn rate limit. Thử lại sau."),
                _ => Result<JsonDocument>.Failure(error)
            };
        }

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions, ct);
        return document is null
            ? Result<JsonDocument>.Failure("AI provider trả về body rỗng.")
            : Result<JsonDocument>.Success(document);
    }

    private static string TryReadProviderError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "AI provider trả về lỗi không có nội dung.";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? "AI provider trả về lỗi.";
                }

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? "AI provider trả về lỗi.";
            }
        }
        catch
        {
            // Fall through to trimmed raw body.
        }

        return body.Length > 500 ? body[..500] : body;
    }

    private sealed class ToolCallBuffer
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public System.Text.StringBuilder Arguments { get; } = new();
    }

    private sealed record CompletedToolCall(string Id, string Name, string Arguments);

    private sealed record StreamRoundResult(JsonObject AssistantMessage, IReadOnlyList<CompletedToolCall> ToolCalls);
}
