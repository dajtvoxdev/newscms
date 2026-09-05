using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Engagement;
using NewsCMS.Application.Engagement.Dtos;
using NewsCMS.Application.Site;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

[Area("Theme")]
public class CommitmentController : Controller
{
    public const string PledgeCookieName = "_nv_pledge_vid";
    private const int WallItemLimit = 500;
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    private readonly ICommitmentService _service;
    private readonly IValidator<CommitmentCreateDto> _validator;
    private readonly ISiteSettingService _settings;

    public CommitmentController(
        ICommitmentService service,
        IValidator<CommitmentCreateDto> validator,
        ISiteSettingService settings)
    {
        _service = service;
        _validator = validator;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var wall = await _service.GetWallAsync(WallItemLimit, ct);
        var totalCount = await _service.GetTotalCountAsync(ct);
        var signerKey = ResolveSignerKey(out _);
        var ipHash = SignatureKeyHelper.IpHashFromRequest(HttpContext);
        var hasSigned = await _service.HasSignedAsync(signerKey, ipHash, ct);

        var settings = await _settings.GetGroupAsync("survey", ct);
        var survey = SurveyConfigDto.FromSettings(settings);

        var vm = new CommitmentPageViewModel(wall, hasSigned, survey, totalCount);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var signerKey = ResolveSignerKey(out _);
        var ipHash = SignatureKeyHelper.IpHashFromRequest(HttpContext);
        var hasSigned = await _service.HasSignedAsync(signerKey, ipHash, ct);
        return Json(new { signed = hasSigned });
    }

    [HttpGet]
    public async Task<IActionResult> Wall(DateTime? sinceUtc, CancellationToken ct)
    {
        var items = sinceUtc.HasValue
            ? await _service.GetWallSinceAsync(sinceUtc.Value, ct)
            : await _service.GetWallAsync(WallItemLimit, ct);
        var totalCount = await _service.GetTotalCountAsync(ct);

        return Json(new
        {
            totalCount,
            items = items.Select(x => new
            {
                id = x.Id,
                name = x.DisplayName,
                message = x.Message,
                signatureUrl = x.SignatureUrl,
                createdAt = x.CreatedAt
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Sign(
        [FromForm] string displayName,
        [FromForm] string message,
        [FromForm] string signatureKind,
        [FromForm] string? signatureBase64,
        IFormFile? uploadedImage,
        CancellationToken ct)
    {
        Stream? imageStream = null;
        try
        {
            if (uploadedImage is not null && uploadedImage.Length > 0)
            {
                imageStream = uploadedImage.OpenReadStream();
            }

            var dto = new CommitmentCreateDto(
                DisplayName: displayName ?? string.Empty,
                Message: message ?? string.Empty,
                SignatureKind: signatureKind ?? string.Empty,
                SignatureBase64: signatureBase64,
                UploadedImageStream: imageStream,
                UploadedImageFileName: uploadedImage?.FileName,
                UploadedImageSize: uploadedImage?.Length,
                UploadedImageContentType: uploadedImage?.ContentType);

            var validation = await _validator.ValidateAsync(dto, ct);
            if (!validation.IsValid)
            {
                var firstError = validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
                return Json(new { ok = false, error = firstError });
            }

            var signerKey = ResolveSignerKey(out var fresh);
            var ipHash = SignatureKeyHelper.IpHashFromRequest(HttpContext);
            var ua = Request.Headers.UserAgent.ToString();

            var result = await _service.CreateAsync(dto, signerKey, ipHash, ua, ct);
            if (!result.Succeeded)
            {
                return Json(new { ok = false, error = result.Error ?? "Không thể lưu cam kết." });
            }
            var totalCount = await _service.GetTotalCountAsync(ct);

            return Json(new
            {
                ok = true,
                id = result.Value,
                totalCount,
                cookieIssued = fresh
            });
        }
        finally
        {
            imageStream?.Dispose();
        }
    }

    private string ResolveSignerKey(out bool freshlyIssued)
    {
        var existing = Request.Cookies[PledgeCookieName];
        if (!string.IsNullOrWhiteSpace(existing) && existing.Length is >= 16 and <= 64)
        {
            freshlyIssued = false;
            return SignatureKeyHelper.Sha256(existing);
        }

        var newId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(PledgeCookieName, newId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(CookieLifetime),
            IsEssential = true
        });
        freshlyIssued = true;
        return SignatureKeyHelper.Sha256(newId);
    }
}

public sealed record CommitmentPageViewModel(
    IReadOnlyList<CommitmentWallItemDto> Wall,
    bool HasSigned,
    SurveyConfigDto Survey,
    int TotalCount);

public sealed record SurveyConfigDto(
    bool Enabled,
    string DisplayMode,
    string Url,
    string Title,
    string Subtitle,
    string Description,
    string CtaLabel)
{
    public const string DefaultTitle = "Chia sẻ góc nhìn của bạn";
    public const string DefaultSubtitle = "Hải Lưu Ngược mong muốn lắng nghe bạn";
    public const string DefaultDescription = "Khảo sát chỉ mất khoảng <strong>2-3 phút</strong>. Mỗi câu trả lời của bạn là một đóng góp giúp chiến dịch hiểu rõ hơn nhận thức cộng đồng về ô nhiễm nhựa biển.";
    public const string DefaultCtaLabel = "Làm khảo sát";
    public const string DisplayModeIframe = "iframe";
    public const string DisplayModeLink = "link";

    public static SurveyConfigDto Empty => new(
        Enabled: false,
        DisplayMode: DisplayModeIframe,
        Url: string.Empty,
        Title: DefaultTitle,
        Subtitle: DefaultSubtitle,
        Description: DefaultDescription,
        CtaLabel: DefaultCtaLabel);

    public static SurveyConfigDto FromSettings(IReadOnlyDictionary<string, string?> dict)
    {
        string Get(string key, string fallback)
            => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        bool enabled = dict.TryGetValue("survey.enabled", out var en)
            && bool.TryParse(en, out var parsed) && parsed;

        var mode = Get("survey.displayMode", DisplayModeIframe);
        if (mode != DisplayModeIframe && mode != DisplayModeLink)
        {
            mode = DisplayModeIframe;
        }

        return new SurveyConfigDto(
            Enabled: enabled,
            DisplayMode: mode,
            Url: Get("survey.url", string.Empty),
            Title: Get("survey.title", DefaultTitle),
            Subtitle: Get("survey.subtitle", DefaultSubtitle),
            Description: Get("survey.description", DefaultDescription),
            CtaLabel: Get("survey.ctaLabel", DefaultCtaLabel));
    }
}
