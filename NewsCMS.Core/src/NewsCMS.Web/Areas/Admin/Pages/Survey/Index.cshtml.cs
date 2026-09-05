using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Site;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Survey;

[Authorize(Permissions.Survey.EditSetting)]
public class IndexModel : PageModel
{
    public const string Group = "survey";

    private static readonly string[] AllowedHosts =
    {
        "docs.google.com",
        "forms.gle"
    };

    private readonly ISiteSettingService _settings;

    public IndexModel(ISiteSettingService settings)
    {
        _settings = settings;
    }

    [BindProperty]
    public SurveyInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var dict = await _settings.GetGroupAsync(Group, ct);
        Input = SurveyInput.FromSettings(dict);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        ValidateInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _settings.SetAsync("survey.enabled", Input.Enabled, Group, ct);
        await _settings.SetAsync("survey.displayMode", NormalizeMode(Input.DisplayMode), Group, ct);
        await _settings.SetAsync("survey.url", Input.Url?.Trim() ?? string.Empty, Group, ct);
        await _settings.SetAsync("survey.title", Input.Title?.Trim() ?? string.Empty, Group, ct);
        await _settings.SetAsync("survey.subtitle", Input.Subtitle?.Trim() ?? string.Empty, Group, ct);
        await _settings.SetAsync("survey.description", Input.Description?.Trim() ?? string.Empty, Group, ct);
        await _settings.SetAsync("survey.ctaLabel", Input.CtaLabel?.Trim() ?? string.Empty, Group, ct);

        TempData["Success"] = "Đã lưu cấu hình khảo sát.";
        return RedirectToPage();
    }

    private void ValidateInput()
    {
        Input.DisplayMode = NormalizeMode(Input.DisplayMode);

        if (Input.Enabled && string.IsNullOrWhiteSpace(Input.Url))
        {
            ModelState.AddModelError("Input.Url", "Phải nhập URL khi bật section.");
        }

        if (!string.IsNullOrWhiteSpace(Input.Url))
        {
            if (!Uri.TryCreate(Input.Url.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("Input.Url",
                    "URL không hợp lệ. Chỉ chấp nhận https://docs.google.com hoặc https://forms.gle.");
            }
        }
    }

    private static string NormalizeMode(string? mode) =>
        string.Equals(mode, "link", StringComparison.OrdinalIgnoreCase) ? "link" : "iframe";

    public sealed class SurveyInput
    {
        [Display(Name = "Bật section")]
        public bool Enabled { get; set; }

        [Display(Name = "Chế độ hiển thị")]
        public string DisplayMode { get; set; } = "iframe";

        [Display(Name = "URL Google Form")]
        [StringLength(500)]
        public string? Url { get; set; }

        [Display(Name = "Tiêu đề")]
        [Required(ErrorMessage = "Vui lòng nhập tiêu đề.")]
        [StringLength(160)]
        public string Title { get; set; } = string.Empty;

        [Display(Name = "Phụ đề")]
        [StringLength(220)]
        public string? Subtitle { get; set; }

        [Display(Name = "Mô tả")]
        [Required(ErrorMessage = "Vui lòng nhập mô tả.")]
        [StringLength(1200)]
        public string Description { get; set; } = string.Empty;

        [Display(Name = "Nhãn nút CTA")]
        [Required(ErrorMessage = "Vui lòng nhập nhãn nút.")]
        [StringLength(80)]
        public string CtaLabel { get; set; } = "Làm khảo sát";

        public static SurveyInput FromSettings(IReadOnlyDictionary<string, string?> dict)
        {
            string Get(string key, string fallback) =>
                dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

            bool enabled = dict.TryGetValue("survey.enabled", out var en)
                && bool.TryParse(en, out var parsed) && parsed;

            return new SurveyInput
            {
                Enabled = enabled,
                DisplayMode = Get("survey.displayMode", "iframe"),
                Url = Get("survey.url", string.Empty),
                Title = Get("survey.title", "Chia sẻ góc nhìn của bạn"),
                Subtitle = Get("survey.subtitle", "Hải Lưu Ngược mong muốn lắng nghe bạn"),
                Description = Get("survey.description",
                    "Khảo sát chỉ mất khoảng <strong>2-3 phút</strong>. Mỗi câu trả lời của bạn là một đóng góp giúp chiến dịch hiểu rõ hơn nhận thức cộng đồng về ô nhiễm nhựa biển."),
                CtaLabel = Get("survey.ctaLabel", "Làm khảo sát")
            };
        }
    }
}
