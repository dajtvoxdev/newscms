using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Ai;

[Authorize(Permissions.Ai.UseAssist)]
public class GenerateModel : PageModel
{
    private readonly IAiCompletionService _completionService;
    private readonly ContentSanitizer _contentSanitizer;
    private readonly ILogger<GenerateModel> _logger;

    public GenerateModel(IAiCompletionService completionService, ContentSanitizer contentSanitizer, ILogger<GenerateModel> logger)
    {
        _completionService = completionService;
        _contentSanitizer = contentSanitizer;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync([FromBody] AiGenerationRequest request)
    {
        try
        {
            var result = await _completionService.GenerateAsync(request);
            if (!result.Succeeded)
                return new JsonResult(new { error = result.Error }) { StatusCode = 400 };

            // Sanitize AI output before returning to client — prevents DOM-XSS in admin preview
            var raw = result.Value!.Content;
            var sanitized = _contentSanitizer.Sanitize(raw);
            return new JsonResult(new { content = sanitized, raw = raw });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed for skill {SkillKey}", request.SkillKey);
            return new JsonResult(new { error = "Lỗi máy chủ khi tạo nội dung." }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Trò chuyện soạn bài nhiều lượt — gọi bằng /Admin/Ai/Generate?handler=Chat.
    /// Trả về 3 phần riêng để client cho người dùng apply từng phần vào form.
    /// </summary>
    public async Task<IActionResult> OnPostChatAsync([FromBody] AiChatRequest request)
    {
        try
        {
            var result = await _completionService.ChatAsync(request);
            if (!result.Succeeded)
                return new JsonResult(new { error = result.Error }) { StatusCode = 400 };

            var value = result.Value!;
            // Body là HTML do AI sinh, sanitize trước khi trả về — cùng lý do như
            // OnPostAsync: client đổ thẳng vào innerHTML của khung xem trước.
            return new JsonResult(new
            {
                reply = value.AssistantText,
                title = value.Title,
                excerpt = value.Excerpt,
                body = _contentSanitizer.Sanitize(value.Body),
                // Ngữ cảnh đã gửi kèm, để khung chat hiện được "AI đang thấy gì".
                siteContext = value.SiteContext
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat failed for content type {ContentType}", request.ContentType);
            return new JsonResult(new { error = "Lỗi máy chủ khi tạo nội dung." }) { StatusCode = 500 };
        }
    }
}
