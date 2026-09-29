using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio;

/// <summary>
/// API JSON của modal tạo ảnh (<c>image-studio.js</c>) — dùng chung cho trang Xưởng ảnh, bài viết,
/// sản phẩm. Gọi bằng <c>/Admin/ImageStudio/Api?handler=…</c>, token chống giả mạo ở header
/// <c>RequestVerificationToken</c>.
/// </summary>
/// <remarks>
/// Xem cần <see cref="Permissions.ImageStudio.View"/>; tạo, đưa vào thư viện và huỷ cần thêm
/// <see cref="Permissions.ImageStudio.Create"/> — kiểm trong handler vì Razor Pages không nhận
/// <c>[Authorize]</c> trên từng handler.
/// </remarks>
[Authorize(Permissions.ImageStudio.View)]
public class ApiModel : PageModel
{
    private readonly IImageStudioService _studio;
    private readonly IImagePromptLibraryService _library;
    private readonly IImagePromptAssistant _assistant;
    private readonly UserManager<AppUser> _users;

    public ApiModel(IImageStudioService studio, IImagePromptLibraryService library, IImagePromptAssistant assistant, UserManager<AppUser> users)
    {
        _studio = studio;
        _library = library;
        _assistant = assistant;
        _users = users;
    }

    /// <summary><c>?handler=Templates&amp;purpose=post-cover</c> — mẫu cho modal, đúng mục đích trước.</summary>
    public async Task<IActionResult> OnGetTemplatesAsync(string? purpose)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        IReadOnlyList<ImagePromptTemplateDto> templates = await _library.GetPublishedAsync(ImagePurposes.Parse(purpose));

        return new JsonResult(new
        {
            templates = templates.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                category = t.Category,
                purpose = t.PurposeKey,
                description = t.Description,
                prompt = t.Prompt,
                aspectRatio = t.AspectRatio,
                isTrend = t.IsTrend,
                trendName = t.TrendName,
                demoUrl = t.DemoImageUrl,
                // {thuong_hieu} server tự điền bằng tên site — modal không hỏi.
                placeholders = t.Placeholders
                    .Where(p => p != ImagePromptTemplate.BrandPlaceholder)
                    .Select(p => new { token = p, label = ImagePromptPlaceholders.Label(p) }),
            }),
        });
    }

    public async Task<IActionResult> OnPostEnhanceAsync([FromBody] EnhanceRequest request)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        Result<string> result = await _assistant.EnhanceAsync(request.Prompt ?? string.Empty, ImagePurposes.Parse(request.Purpose) ?? ImagePurpose.Free);

        return result.Succeeded ? new JsonResult(new { prompt = result.Value }) : Error(result.Error);
    }

    public async Task<IActionResult> OnPostSuggestAsync([FromBody] SuggestRequest request)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        Result<ImagePromptSuggestionDto> result = await _assistant.SuggestAsync(new ImagePromptSuggestInput(
            ImagePurposes.Parse(request.Purpose) ?? ImagePurpose.Free, request.Title, request.Excerpt));

        return result.Succeeded
            ? new JsonResult(new { prompt = result.Value!.Prompt, alt = result.Value.Alt, caption = result.Value.Caption })
            : Error(result.Error);
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnGetFormAsync()
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        ImageStudioFormDto form = await _studio.GetFormAsync(CurrentUserId());

        return new JsonResult(new
        {
            enabled = form.Enabled,
            disabledReason = form.DisabledReason,
            models = form.Models.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                description = m.Description,
                pricePerImageUsd = m.PricePerImageUsd,
                maxVariants = m.MaxVariants,
                isDefault = m.IsDefault,
            }),
            aspectRatios = form.AspectRatios,
            monthlyRemaining = form.MonthlyRemaining,
            dailyRemaining = form.DailyRemaining,
            coverAspect = form.CoverAspect,
            productAspect = form.ProductAspect,
            idempotencyKey = Guid.NewGuid().ToString("N"),
        });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] CreateRequest request)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        ImagePurpose purpose = ImagePurposes.Parse(request.Purpose) ?? ImagePurpose.Free;

        Result<ImageJobDto> result = await _studio.CreateAsync(new ImageJobCreateInput(
            request.IdempotencyKey ?? string.Empty,
            request.ModelId,
            request.Prompt ?? string.Empty,
            request.AspectRatio ?? "1:1",
            request.VariantCount,
            purpose,
            request.TemplateId,
            request.ContextType,
            request.ContextId), CurrentUserId());

        return result.Succeeded ? new JsonResult(ToJson(result.Value!)) : Error(result.Error);
    }

    /// <summary><c>?handler=Status&amp;ids=a,b,c</c> — modal hỏi định kỳ tới khi mọi job xong.</summary>
    public async Task<IActionResult> OnGetStatusAsync(string? ids)
    {
        List<Guid> jobIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out Guid g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        IReadOnlyList<ImageJobDto> jobs = await _studio.GetJobsAsync(jobIds);

        return new JsonResult(new { jobs = jobs.Select(ToJson) });
    }

    public async Task<IActionResult> OnPostPromoteAsync([FromBody] PromoteRequest request)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        Result<ImagePromoteResultDto> result = await _studio.PromoteAsync(request.OutputId, CurrentUserId(), request.AltText);

        if (!result.Succeeded)
        {
            return Error(result.Error);
        }

        ImagePromoteResultDto media = result.Value!;

        return new JsonResult(new { id = media.MediaId, url = media.Url, alt = media.AltText, width = media.Width, height = media.Height });
    }

    public async Task<IActionResult> OnPostCancelAsync([FromBody] CancelRequest request)
    {
        if (!CanCreate())
        {
            return Forbidden();
        }

        Result result = await _studio.CancelAsync(request.JobId);

        return result.Succeeded ? new JsonResult(new { ok = true }) : Error(result.Error);
    }

    private bool CanCreate() => User.IsInRole("SuperAdmin") || User.HasClaim(Permissions.Prefix, Permissions.ImageStudio.Create);

    private Guid CurrentUserId() => Guid.TryParse(_users.GetUserId(User), out Guid id) ? id : Guid.Empty;

    private static JsonResult Forbidden() =>
        new(new { error = "Tài khoản của bạn chưa có quyền tạo ảnh AI (ImageStudio.Image.Create)." }) { StatusCode = StatusCodes.Status403Forbidden };

    private static JsonResult Error(string? message) =>
        new(new { error = message ?? "Có lỗi xảy ra." }) { StatusCode = StatusCodes.Status400BadRequest };

    internal static object ToJson(ImageJobDto job) => new
    {
        id = job.Id,
        status = job.Status.ToString().ToLowerInvariant(),
        finished = job.IsFinished,
        modelName = job.ModelName,
        prompt = job.UserPrompt,
        aspectRatio = job.AspectRatio,
        variantCount = job.VariantCount,
        estimatedCostUsd = job.EstimatedCostUsd,
        costUsd = job.CostUsd,
        error = job.Error,
        outputs = job.Outputs.Select(o => new
        {
            id = o.Id,
            url = o.Url,
            width = o.Width,
            height = o.Height,
            promotedMediaId = o.PromotedMediaId,
            purged = o.IsPurged,
        }),
    };

    public sealed class CreateRequest
    {
        public string? IdempotencyKey { get; set; }
        public Guid ModelId { get; set; }
        public string? Prompt { get; set; }
        public string? AspectRatio { get; set; }
        public int VariantCount { get; set; } = 1;
        public string? Purpose { get; set; }
        public Guid? TemplateId { get; set; }
        public string? ContextType { get; set; }
        public Guid? ContextId { get; set; }
    }

    public sealed class PromoteRequest
    {
        public Guid OutputId { get; set; }
        public string? AltText { get; set; }
    }

    public sealed class CancelRequest
    {
        public Guid JobId { get; set; }
    }

    public sealed class EnhanceRequest
    {
        public string? Prompt { get; set; }
        public string? Purpose { get; set; }
    }

    public sealed class SuggestRequest
    {
        public string? Purpose { get; set; }
        public string? Title { get; set; }
        public string? Excerpt { get; set; }
    }
}
