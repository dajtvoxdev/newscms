using NewsCMS.Application.Common;

namespace NewsCMS.Application.VideoStudio;

/// <summary>
/// Kết nối NewsCMS ↔ AdVideo: địa chỉ + operator key (toàn hệ thống), và liên kết từng site với một tenant.
/// </summary>
public interface IAdVideoConnectionService
{
    Task<AdVideoConnectionDto> GetAsync(CancellationToken ct = default);
    Task<Result> SaveAsync(AdVideoConnectionInput input, CancellationToken ct = default);

    /// <summary>Gọi <c>/healthz</c> và một endpoint quản trị — kiểm cả đường mạng lẫn operator key.</summary>
    Task<Result<AdVideoHealthDto>> TestAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AdVideoSiteLinkDto>> GetSiteLinksAsync(CancellationToken ct = default);

    /// <summary>Tạo tenant bên AdVideo cho site và cất tenant key (đã mã hoá). Key không đi ra màn hình.</summary>
    Task<Result> LinkSiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Cấp key mới cho tenant của site. Key cũ chết ngay bên AdVideo.</summary>
    Task<Result> RotateSiteKeyAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Tạm ngừng tenant bên AdVideo và bỏ liên kết. Video cũ vẫn nằm bên AdVideo.</summary>
    Task<Result> UnlinkSiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Site hiện tại đã kết nối AdVideo chưa — trang VideoStudio dùng để hiện hướng dẫn thay vì lỗi.</summary>
    Task<bool> IsCurrentSiteLinkedAsync(CancellationToken ct = default);
}

/// <summary>API quản trị AdVideo (<c>/v1/admin</c>), gọi bằng operator key đã lưu.</summary>
public interface IAdVideoAdminClient
{
    Task<Result<IReadOnlyList<AdVideoSettingDto>>> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<AdVideoSettingDto>> UpdateSettingAsync(string key, string value, CancellationToken ct = default);

    Task<Result<IReadOnlyList<AdVideoCredentialDto>>> GetCredentialsAsync(CancellationToken ct = default);
    Task<Result<AdVideoCredentialSaved>> SaveCredentialAsync(AdVideoCredentialInput input, CancellationToken ct = default);
    Task<Result> DeactivateCredentialAsync(string provider, string? reason, CancellationToken ct = default);

    Task<Result<IReadOnlyList<AdVideoDescriptorDto>>> GetDescriptorsAsync(string? provider = null, CancellationToken ct = default);
    Task<Result<AdVideoDescriptorDto>> GetDescriptorAsync(string provider, int version, CancellationToken ct = default);
    Task<Result<AdVideoDescriptorDto>> AddDescriptorAsync(string descriptorJson, string? note, CancellationToken ct = default);
    Task<Result<AdVideoDescriptorPreviewDto>> PreviewDescriptorAsync(string provider, int version, CancellationToken ct = default);
    Task<Result> ActivateDescriptorAsync(string provider, int version, CancellationToken ct = default);
    Task<Result> DeactivateDescriptorAsync(string provider, CancellationToken ct = default);

    Task<Result<IReadOnlyList<AdVideoPromptDto>>> GetPromptsAsync(CancellationToken ct = default);
    Task<Result<AdVideoPromptDto>> AddPromptVersionAsync(AdVideoPromptInput input, CancellationToken ct = default);
    Task<Result> ActivatePromptAsync(string code, int version, CancellationToken ct = default);

    Task<Result<IReadOnlyList<AdVideoTenantDto>>> GetTenantsAsync(CancellationToken ct = default);
    Task<Result<AdVideoIssuedTenantKey>> CreateTenantAsync(string name, string? note, CancellationToken ct = default);
    Task<Result<AdVideoIssuedTenantKey>> RotateTenantKeyAsync(Guid tenantId, CancellationToken ct = default);
    Task<Result> SetTenantActiveAsync(Guid tenantId, bool isActive, string? reason, CancellationToken ct = default);

    Task<Result<AdVideoLabelFontDto>> GetLabelFontAsync(CancellationToken ct = default);
    Task<Result<AdVideoLabelFontDto>> UploadLabelFontAsync(Stream content, string fileName, CancellationToken ct = default);
    Task<Result> ResetLabelFontAsync(CancellationToken ct = default);
}

/// <summary>API video của AdVideo cho <b>site hiện tại</b>, gọi bằng tenant key của site đó.</summary>
public interface IAdVideoClient
{
    Task<Result<AdVideoUploadDto>> UploadImageAsync(Stream content, string fileName, CancellationToken ct = default);
    Task<Result<AdVideoJobDto>> CreateJobAsync(CreateAdVideoInput input, CancellationToken ct = default);
    Task<Result<AdVideoJobPageDto>> ListJobsAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<Result<AdVideoJobDto>> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Result<AdVideoJobDto>> CancelJobAsync(Guid jobId, CancellationToken ct = default);
}
