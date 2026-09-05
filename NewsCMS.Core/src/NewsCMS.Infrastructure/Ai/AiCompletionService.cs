using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ai;

public sealed class AiCompletionService : IAiCompletionService
{
    private readonly AppDbContext _db;
    private readonly IAiChatClient _chatClient;
    private readonly IAiToolRegistry _toolRegistry;
    private readonly IDataProtector _protector;
    private readonly ILogger<AiCompletionService> _logger;

    public AiCompletionService(AppDbContext db, IAiChatClient chatClient, IAiToolRegistry toolRegistry,
        IDataProtectionProvider dp, ILogger<AiCompletionService> logger)
    {
        _db = db;
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _protector = dp.CreateProtector("NewsCMS.Ai.ApiKey");
        _logger = logger;
    }

    public async Task<Result<AiGenerationResult>> GenerateAsync(AiGenerationRequest request, CancellationToken ct = default)
    {
        // Resolve connection — only pick connections that have an API key configured
        var connection = request.ConnectionId.HasValue
            ? await _db.AiConnections.FirstOrDefaultAsync(
                x => x.Id == request.ConnectionId.Value && x.IsActive && !x.IsDeleted, ct)
            : await _db.AiConnections.FirstOrDefaultAsync(
                x => x.IsDefault && x.IsActive && !x.IsDeleted, ct);

        if (connection == null)
            return Result<AiGenerationResult>.Failure("Không tìm thấy kết nối AI khả dụng.");

        // Load skill
        var skill = await _db.AiSkills.FirstOrDefaultAsync(x => x.Key == request.SkillKey && x.IsActive && !x.IsDeleted, ct);
        if (skill == null || skill.Kind != AiSkillKind.Prompt)
            return Result<AiGenerationResult>.Failure("Không tìm thấy skill hoặc skill không hợp lệ.");

        // Decrypt API key — guard against empty/corrupt payload
        string apiKey;
        try
        {
            if (string.IsNullOrEmpty(connection.ApiKeyEncrypted))
                return Result<AiGenerationResult>.Failure("Kết nối AI chưa được cấu hình API key.");
            apiKey = _protector.Unprotect(connection.ApiKeyEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key for connection {ConnectionId}", connection.Id);
            return Result<AiGenerationResult>.Failure("Không thể giải mã API key của kết nối. Vui lòng cập nhật lại key.");
        }

        // Build tools if UseTools
        var tools = new List<AiToolDefinition>();
        if (skill.UseTools)
        {
            var toolSkills = await _db.AiSkills
                .Where(x => x.Kind == AiSkillKind.Tool && x.IsActive && !x.IsDeleted)
                .ToListAsync(ct);

            foreach (var ts in toolSkills)
            {
                var tool = _toolRegistry.Find(ts.ToolType!);
                if (tool == null) continue;

                string? toolApiKey = null;
                if (!string.IsNullOrEmpty(ts.ApiKeyEncrypted))
                {
                    try
                    {
                        toolApiKey = _protector.Unprotect(ts.ApiKeyEncrypted);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key for tool skill {SkillKey}. Skipping tool.", ts.Key);
                        continue; // Skip this tool rather than failing the whole request
                    }
                }

                var ctx = new AiToolContext(toolApiKey, ts.BaseUrl, ts.ConfigJson);

                tools.Add(new AiToolDefinition(
                    tool.Key,
                    tool.Description,
                    tool.ParametersJsonSchema,
                    async (args, cancelToken) => await tool.ExecuteAsync(args, ctx, cancelToken)));
            }
        }

        // Build style instruction
        var styleInstruction = request.Style == AiGenerationStyle.Styled
            ? "HTML trang trí, được dùng inline style + class ai-* (ai-callout, ai-card, ai-grid, ai-lead, ai-quote)"
            : "HTML ngữ nghĩa, không trang trí";

        // Fill placeholders in user prompt template
        var userPrompt = (skill.UserPromptTemplate ?? "")
            .Replace("{title}", request.Title ?? "")
            .Replace("{content}", request.Content ?? "")
            .Replace("{selection}", request.Selection ?? "")
            .Replace("{language}", request.Language ?? "vi")
            .Replace("{styleInstruction}", styleInstruction);

        // Build messages
        var messages = new List<(string, string)>();
        if (!string.IsNullOrEmpty(skill.SystemPrompt))
            messages.Add(("system", skill.SystemPrompt));
        messages.Add(("user", userPrompt));

        // Call AI
        var result = await _chatClient.CompleteAsync(
            connection.BaseUrl, apiKey, connection.DefaultModel,
            messages, tools,
            skill.Temperature, skill.MaxTokens,
            connection.TimeoutSeconds, ct);

        if (!result.Succeeded && tools.Count > 0)
        {
            _logger.LogWarning(
                "AI generation with tools failed for skill {SkillKey} on connection {ConnectionId}. Retrying without tools. Error: {Error}",
                skill.Key,
                connection.Id,
                result.Error);

            result = await _chatClient.CompleteAsync(
                connection.BaseUrl, apiKey, connection.DefaultModel,
                messages, Array.Empty<AiToolDefinition>(),
                skill.Temperature, skill.MaxTokens,
                connection.TimeoutSeconds, ct);
        }

        if (!result.Succeeded)
            return Result<AiGenerationResult>.Failure(result.Error ?? "Lỗi không xác định.");

        return Result<AiGenerationResult>.Success(new AiGenerationResult(result.Value!));
    }
}
