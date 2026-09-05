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
}
