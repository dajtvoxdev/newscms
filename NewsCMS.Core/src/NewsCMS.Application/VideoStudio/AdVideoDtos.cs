using System.Text.Json;

namespace NewsCMS.Application.VideoStudio;

// Bản sao phía NewsCMS của hợp đồng JSON AdVideo (snake_case trên dây, PascalCase ở đây).
// Tách khỏi AdVideo.Api có chủ đích: hai solution, hai DB, deploy độc lập — NewsCMS không tham
// chiếu assembly nào của AdVideo.

// ---------------------------------------------------------------- kết nối

public record AdVideoConnectionDto(
    bool IsConfigured,
    string? BaseUrl,
    bool HasOperatorKey,
    string? OperatorKeyPrefix,
    int TimeoutSeconds,
    DateTime? UpdatedAt);

/// <param name="OperatorKey">Để trống = giữ key đang có.</param>
public record AdVideoConnectionInput(string BaseUrl, string? OperatorKey, int TimeoutSeconds = 60);

public record AdVideoHealthDto(string Status, IReadOnlyDictionary<string, string> Checks);

public record AdVideoSiteLinkDto(
    Guid SiteId,
    string SiteName,
    string SiteSlug,
    bool IsLinked,
    Guid? TenantId,
    string? ApiKeyPrefix,
    DateTime? LinkedAt);

// ---------------------------------------------------------------- quản trị

public record AdVideoSettingDto(
    string Key,
    string Value,
    string ValueType,
    string Description,
    bool IsProvisional,
    string? MinValue,
    string? MaxValue,
    DateTime? UpdatedAt);

public record AdVideoCredentialDto(
    Guid Id,
    string Provider,
    string ModelId,
    string Category,
    string? EndpointUrl,
    bool HasKey,
    string MaskedKey,
    bool IsActive,
    int Priority,
    string? Notes,
    JsonElement? Capability,
    DateTime? UpdatedAt);

/// <param name="ApiKey">Để trống = giữ key cũ. Không bao giờ đọc ngược ra được.</param>
/// <param name="CapabilityJson">JSON object; để trống = AdVideo lấy từ descriptor/manifest.</param>
public record AdVideoCredentialInput(
    string Provider,
    string? ApiKey,
    string? ModelId,
    string? EndpointUrl,
    string? CapabilityJson,
    int? Priority,
    bool? IsActive,
    string? Note);

public record AdVideoCredentialSaved(AdVideoCredentialDto Credential, bool KeyChanged, IReadOnlyList<string> Notices);

public record AdVideoDescriptorDto(
    Guid Id,
    string Code,
    int Version,
    string Kind,
    bool IsActive,
    string Sha256,
    string? ChangeNote,
    DateTime CreatedAt,
    string? DescriptorJson);

public record AdVideoDescriptorPreviewDto(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    IReadOnlyList<string> Warnings);

public record AdVideoPromptDto(
    Guid Id,
    string Code,
    int Version,
    string Kind,
    bool IsActive,
    string Content,
    string? FormatCode,
    string? ChangeNote,
    DateTime CreatedAt);

public record AdVideoPromptInput(string Code, string Kind, string Content, string ChangeNote, string? FormatCode, bool Activate);

public record AdVideoTenantDto(
    Guid Id,
    string Name,
    string ApiKeyPrefix,
    bool IsActive,
    string? Note,
    DateTime CreatedAt,
    DateTime? ApiKeyRotatedAt);

/// <summary>Chỉ đi giữa AdVideo và service kết nối — không bao giờ tới Razor Page.</summary>
public record AdVideoIssuedTenantKey(AdVideoTenantDto Tenant, string ApiKey);

public record AdVideoLabelFontDto(
    string Source,
    string? ObjectKey,
    string? Sha256,
    long? SizeBytes,
    string? Format,
    IReadOnlyList<string> MissingCharacters);

// ---------------------------------------------------------------- video của site

public record AdVideoUploadDto(Guid AssetId, string ContentType, long SizeBytes, string Sha256, bool Reused);

public record AdVideoJobDto(
    Guid JobId,
    string Status,
    int CurrentStep,
    string? StepName,
    int ProgressPercent,
    string? Quality,
    string? AspectRatio,
    int DurationSeconds,
    string? Provider,
    decimal EstimatedCostUsd,
    decimal ActualCostUsd,
    string? FailureReason,
    string? DownloadUrl,
    DateTime CreatedAt,
    DateTime? EstimatedReadyAt,
    DateTime? CompletedAt)
{
    /// <summary>Không tiến triển thêm nữa — trang chi tiết thôi tự làm mới.</summary>
    public bool IsTerminal => Status is "completed" or "failed" or "cancelled";
}

public record AdVideoJobPageDto(IReadOnlyList<AdVideoJobDto> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Brief tạo video. <see cref="IdempotencyKey"/> sinh lúc mở form — bấm hai lần không thành hai job.</summary>
public record CreateAdVideoInput(
    string IdempotencyKey,
    string? ProductName,
    string Prompt,
    string Script,
    int DurationSeconds,
    string AspectRatio,
    string Quality,
    bool HasPerson,
    string NativeSound,
    IReadOnlyList<Guid> ProductImageIds);
