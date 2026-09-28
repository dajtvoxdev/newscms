using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.AdVideo;

/// <summary>Key provider (credential) và provider khai báo bằng JSON (descriptor).</summary>
[Authorize(Roles = "SuperAdmin")]
public class ProvidersModel : PageModel
{
    private readonly IAdVideoAdminClient _admin;

    public ProvidersModel(IAdVideoAdminClient admin) => _admin = admin;

    public IReadOnlyList<AdVideoCredentialDto> Credentials { get; private set; } = Array.Empty<AdVideoCredentialDto>();
    public IReadOnlyList<AdVideoDescriptorDto> Descriptors { get; private set; } = Array.Empty<AdVideoDescriptorDto>();

    /// <summary>Lỗi khi đọc từ AdVideo — hiện ngay trên trang thay vì một trang trống khó hiểu.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Kết quả chạy khô của một bản descriptor, nếu vừa bấm "Chạy khô".</summary>
    public AdVideoDescriptorPreviewDto? Preview { get; private set; }
    public string? PreviewOf { get; private set; }

    [BindProperty]
    public CredentialForm Credential { get; set; } = new();

    [BindProperty]
    public DescriptorForm Descriptor { get; set; } = new();

    public async Task OnGetAsync(string? preview, int? version)
    {
        await LoadAsync();

        if (!string.IsNullOrWhiteSpace(preview) && version is { } v)
        {
            Result<AdVideoDescriptorPreviewDto> result = await _admin.PreviewDescriptorAsync(preview, v);

            if (result.Succeeded)
            {
                Preview = result.Value;
                PreviewOf = $"{preview} bản {v}";
            }
            else
            {
                TempData["Error"] = result.Error;
            }
        }
    }

    public async Task<IActionResult> OnPostSaveCredentialAsync()
    {
        if (string.IsNullOrWhiteSpace(Credential.Provider))
        {
            TempData["Error"] = "Nhập tên provider (ví dụ kling, elevenlabs, hoặc tên descriptor).";
            return RedirectToPage();
        }

        Result<AdVideoCredentialSaved> result = await _admin.SaveCredentialAsync(new AdVideoCredentialInput(
            Credential.Provider,
            Credential.ApiKey,
            Credential.ModelId,
            Credential.EndpointUrl,
            Credential.CapabilityJson,
            Credential.Priority,
            Credential.IsActive,
            Credential.Note));

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage();
        }

        string notices = string.Join(" ", result.Value!.Notices);
        TempData["Success"] = $"Đã lưu {result.Value.Credential.Provider}/{result.Value.Credential.ModelId}"
            + (result.Value.KeyChanged ? $" với key {result.Value.Credential.MaskedKey}." : ", giữ key cũ.")
            + (notices.Length > 0 ? " " + notices : string.Empty);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCredentialAsync(string provider, string modelId, bool activate)
    {
        Result result = activate
            ? await _admin.SaveCredentialAsync(new AdVideoCredentialInput(provider, null, modelId, null, null, null, true, null))
            : await _admin.DeactivateCredentialAsync(provider, $"Tắt từ NewsCMS bởi {User.Identity?.Name}");

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (activate ? $"Đã bật {provider}." : $"Đã tắt {provider}.")
            : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddDescriptorAsync()
    {
        if (string.IsNullOrWhiteSpace(Descriptor.Json))
        {
            TempData["Error"] = "Dán nội dung descriptor (JSON, giữ được comment).";
            return RedirectToPage();
        }

        Result<AdVideoDescriptorDto> result = await _admin.AddDescriptorAsync(Descriptor.Json, Descriptor.Note);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage();
        }

        TempData["Success"] = $"Đã lưu descriptor {result.Value!.Code} bản {result.Value.Version} ở trạng thái TẮT. Chạy khô, nạp key, rồi bật.";

        return RedirectToPage(new { preview = result.Value.Code, version = result.Value.Version });
    }

    public async Task<IActionResult> OnPostActivateDescriptorAsync(string provider, int version)
    {
        Result result = await _admin.ActivateDescriptorAsync(provider, version);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Đã bật {provider} bản {version}." : result.Error;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateDescriptorAsync(string provider)
    {
        Result result = await _admin.DeactivateDescriptorAsync(provider);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã tắt descriptor {provider}; provider quay về adapter viết tay nếu có."
            : result.Error;

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Result<IReadOnlyList<AdVideoCredentialDto>> credentials = await _admin.GetCredentialsAsync();
        Result<IReadOnlyList<AdVideoDescriptorDto>> descriptors = await _admin.GetDescriptorsAsync();

        Credentials = credentials.Value ?? Array.Empty<AdVideoCredentialDto>();
        Descriptors = descriptors.Value ?? Array.Empty<AdVideoDescriptorDto>();
        LoadError = credentials.Error ?? descriptors.Error;
    }

    public sealed class CredentialForm
    {
        public string Provider { get; set; } = "";
        public string? ApiKey { get; set; }
        public string? ModelId { get; set; }
        public string? EndpointUrl { get; set; }
        public string? CapabilityJson { get; set; }
        public int? Priority { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }
    }

    public sealed class DescriptorForm
    {
        public string? Json { get; set; }
        public string? Note { get; set; }
    }
}
