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
        var resolved = await ResolveConnectionAsync(request.ConnectionId, ct);
        if (!resolved.Succeeded) return Result<AiGenerationResult>.Failure(resolved.Error!);
        var (connection, apiKey) = resolved.Value;

        // Load skill
        var skill = await _db.AiSkills.FirstOrDefaultAsync(x => x.Key == request.SkillKey && x.IsActive && !x.IsDeleted, ct);
        if (skill == null || skill.Kind != AiSkillKind.Prompt)
            return Result<AiGenerationResult>.Failure("Không tìm thấy skill hoặc skill không hợp lệ.");

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

    /// <summary>
    /// Trò chuyện nhiều lượt soạn bài. Khác GenerateAsync ở ba điểm:
    /// nhận cả lịch sử hội thoại, trả về 3 phần riêng (tiêu đề/tóm tắt/nội dung)
    /// và luôn kèm hợp đồng JSON ở system prompt để client tách được từng phần.
    /// </summary>
    public async Task<Result<AiChatResult>> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            return Result<AiChatResult>.Failure("Chưa có nội dung hội thoại.");

        var resolved = await ResolveConnectionAsync(request.ConnectionId, ct);
        if (!resolved.Succeeded) return Result<AiChatResult>.Failure(resolved.Error!);
        var (connection, apiKey) = resolved.Value;

        // Skill có thể chưa được seed (DB cũ) — vẫn chạy bằng prompt mặc định
        // thay vì chặn người dùng, vì đây là tính năng soạn thảo chính.
        var skill = await _db.AiSkills.FirstOrDefaultAsync(
            x => x.Key == AiTaskKeys.ArticleChat && x.IsActive && !x.IsDeleted, ct);

        var isProduct = string.Equals(request.ContentType, "product", StringComparison.OrdinalIgnoreCase);
        var labels = isProduct
            ? (Subject: "sản phẩm", Title: "tên sản phẩm", Excerpt: "mô tả ngắn", Body: "mô tả chi tiết")
            : (Subject: "bài viết", Title: "tiêu đề", Excerpt: "tóm tắt", Body: "nội dung");

        var systemPrompt = string.IsNullOrWhiteSpace(skill?.SystemPrompt)
            ? DefaultChatSystemPrompt
            : skill!.SystemPrompt!;

        // Hợp đồng JSON nối ở đây chứ không nằm trong DB: quản trị viên sửa
        // SystemPrompt trong màn Kỹ năng AI cũng không làm vỡ bộ tách kết quả.
        systemPrompt += "\n\nBẮT BUỘC: mỗi lần trả lời chỉ xuất MỘT đối tượng JSON, không kèm chữ nào ngoài JSON, "
            + "không bọc trong markdown, theo đúng cấu trúc:\n"
            + "{\"reply\": \"lời nhắn ngắn gọn cho người dùng\", "
            + "\"title\": \"" + labels.Title + "\", "
            + "\"excerpt\": \"" + labels.Excerpt + "\", "
            + "\"body\": \"HTML của " + labels.Body + "\"}\n"
            + "Trường \"body\" là HTML ngữ nghĩa (thẻ p, h2, h3, ul, li, strong, em, blockquote), "
            + "KHÔNG bọc trong ``` và không dùng thẻ html/head/body. "
            + "Luôn điền đủ cả 3 trường; giữ nguyên phần người dùng chưa yêu cầu sửa.";

        var messages = new List<(string, string)> { ("system", systemPrompt) };

        // Chỉ gửi 20 lượt gần nhất: hội thoại dài làm tăng chi phí mà ngữ cảnh
        // cũ thường không còn cần thiết sau khi người dùng đã sửa xong hướng đi.
        foreach (var m in request.Messages.TakeLast(20))
        {
            var role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            if (!string.IsNullOrWhiteSpace(m.Content))
                messages.Add((role, m.Content));
        }

        // Trạng thái form hiện tại: AI cần thấy bản nháp để "sửa tiếp" thay vì viết lại từ đầu.
        var draft = new System.Text.StringBuilder();
        draft.AppendLine("--- BẢN NHÁP HIỆN TẠI TRÊN FORM ---");
        draft.AppendLine(labels.Title + ": " + (request.CurrentTitle ?? "(trống)"));
        draft.AppendLine(labels.Excerpt + ": " + (request.CurrentExcerpt ?? "(trống)"));
        draft.AppendLine(labels.Body + ":");
        draft.AppendLine(string.IsNullOrWhiteSpace(request.CurrentBody) ? "(trống)" : request.CurrentBody);
        draft.AppendLine("--- HẾT BẢN NHÁP ---");
        draft.AppendLine();
        draft.AppendLine("Hãy trả về JSON đúng định dạng đã mô tả trong system prompt, gồm 3 phần "
            + labels.Title + ", " + labels.Excerpt + ", " + labels.Body + " đã cập nhật theo yêu cầu mới nhất.");
        messages.Add(("user", draft.ToString()));

        var result = await _chatClient.CompleteAsync(
            connection.BaseUrl, apiKey, connection.DefaultModel,
            messages, Array.Empty<AiToolDefinition>(),
            skill?.Temperature, skill?.MaxTokens,
            connection.TimeoutSeconds, ct);

        if (!result.Succeeded)
            return Result<AiChatResult>.Failure(result.Error ?? "Lỗi không xác định.");

        return Result<AiChatResult>.Success(ParseChatResult(result.Value!, isProduct));
    }

    /// <summary>Uỷ quyền cho AiChatResponseParser (thuần, có unit test).</summary>
    private AiChatResult ParseChatResult(string raw, bool isProduct)
    {
        var (result, parsedStructured) = AiChatResponseParser.Parse(raw, isProduct);
        if (!parsedStructured)
            _logger.LogWarning("AI chat trả về nội dung không tách được thành JSON, dùng nguyên văn làm nội dung.");

        return result;
    }

    /// <summary>Kết nối mặc định (hoặc chỉ định) + API key đã giải mã.</summary>
    private async Task<Result<(AiConnection Connection, string ApiKey)>> ResolveConnectionAsync(Guid? connectionId, CancellationToken ct)
    {
        var connection = connectionId.HasValue
            ? await _db.AiConnections.FirstOrDefaultAsync(
                x => x.Id == connectionId.Value && x.IsActive && !x.IsDeleted, ct)
            : await _db.AiConnections.FirstOrDefaultAsync(
                x => x.IsDefault && x.IsActive && !x.IsDeleted, ct);

        if (connection == null)
            return Result<(AiConnection, string)>.Failure("Không tìm thấy kết nối AI khả dụng.");

        if (string.IsNullOrEmpty(connection.ApiKeyEncrypted))
            return Result<(AiConnection, string)>.Failure("Kết nối AI chưa được cấu hình API key.");

        try
        {
            var apiKey = _protector.Unprotect(connection.ApiKeyEncrypted);
            return Result<(AiConnection, string)>.Success((connection, apiKey));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key for connection {ConnectionId}", connection.Id);
            return Result<(AiConnection, string)>.Failure("Không thể giải mã API key của kết nối. Vui lòng cập nhật lại key.");
        }
    }

    /// <summary>
    /// Dùng khi DB chưa có skill article_chat. Hợp đồng JSON nằm ngay trong đây
    /// nên dù quản trị viên sửa SystemPrompt trong DB thế nào, code vẫn luôn nối
    /// thêm phần định dạng bên dưới — không phụ thuộc nội dung admin nhập.
    /// </summary>
    private const string DefaultChatSystemPrompt =
        "Bạn là biên tập viên người Việt, giúp soạn thảo nội dung cho website. "
        + "Trả lời bằng tiếng Việt, thân thiện và ngắn gọn trong trường \"reply\".";
}

