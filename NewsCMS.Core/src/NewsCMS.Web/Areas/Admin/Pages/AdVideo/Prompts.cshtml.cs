using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>Kho prompt của AdVideo: thêm phiên bản, bật, quay lại bản cũ.</summary>
[Authorize(Roles = "SuperAdmin")]
public class PromptsModel : PageModel
{
    public static readonly IReadOnlyList<(string Value, string Label)> Kinds =
    [
        ("global_negative", "Negative toàn cục"),
        ("director", "Đạo diễn (LLM)"),
        ("format", "Theo định dạng"),
        ("quality_check", "Kiểm chất lượng"),
    ];

    private readonly IAdVideoAdminClient _admin;

    public PromptsModel(IAdVideoAdminClient admin) => _admin = admin;

    public IReadOnlyList<IGrouping<string, AdVideoPromptDto>> Groups { get; private set; } = Array.Empty<IGrouping<string, AdVideoPromptDto>>();
    public string? LoadError { get; private set; }

    [BindProperty]
    public PromptForm Input { get; set; } = new();

    public async Task OnGetAsync(string? code)
    {
        Result<IReadOnlyList<AdVideoPromptDto>> result = await _admin.GetPromptsAsync();

        Groups = (result.Value ?? Array.Empty<AdVideoPromptDto>()).GroupBy(p => p.Code).ToList();
        LoadError = result.Error;

        if (!string.IsNullOrWhiteSpace(code) && Groups.FirstOrDefault(g => g.Key == code) is { } group)
        {
            AdVideoPromptDto latest = group.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.Version).First();
            Input = new PromptForm { Code = latest.Code, Kind = latest.Kind, Content = latest.Content, FormatCode = latest.FormatCode };
        }
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        Result<AdVideoPromptDto> result = await _admin.AddPromptVersionAsync(
            new AdVideoPromptInput(Input.Code ?? "", Input.Kind ?? "", Input.Content ?? "", Input.ChangeNote ?? "", Input.FormatCode, Input.Activate));

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã thêm {result.Value!.Code} bản {result.Value.Version}{(result.Value.IsActive ? " và bật" : " (đang tắt)")}."
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostActivateAsync(string code, int version)
    {
        Result result = await _admin.ActivatePromptAsync(code, version);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Đã bật {code} bản {version}." : result.Error;

        return RedirectToPage();
    }

    public sealed class PromptForm
    {
        public string? Code { get; set; }
        public string? Kind { get; set; } = "director";
        public string? Content { get; set; }
        public string? ChangeNote { get; set; }
        public string? FormatCode { get; set; }
        public bool Activate { get; set; } = true;
    }
}
