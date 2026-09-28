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

// ---------------------------------------------------------------- giọng đọc (quản trị)

/// <summary>Giọng có sẵn cho mọi site. Giọng đầu tiên (theo <see cref="SortOrder"/>) của một engine là giọng mặc định.</summary>
public record AdVideoVoicePresetDto(
    Guid Id,
    string Name,
    string? Description,
    string Provider,
    string ProviderVoiceId,
    string? PreviewUrl,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt);

/// <summary>Thêm / sửa giọng có sẵn. Khi sửa, <see cref="Provider"/> và <see cref="ProviderVoiceId"/> bị bỏ qua.</summary>
public record AdVideoVoicePresetInput(
    string? Name,
    string? Description,
    string? Provider,
    string? ProviderVoiceId,
    string? PreviewUrl,
    int? SortOrder,
    bool? IsActive);

/// <summary>Một giọng trong thư viện tài khoản engine (đã trừ giọng clone của khách).</summary>
public record AdVideoProviderVoiceDto(
    string VoiceId,
    string Name,
    string? Category,
    string? Description,
    string? PreviewUrl,
    IReadOnlyDictionary<string, string>? Labels,
    bool AlreadyAdded);

public record AdVideoClonedVoiceStatsDto(Guid TenantId, string TenantName, int Count);

// ---------------------------------------------------------------- giọng đọc (site)

/// <param name="Kind"><c>preset</c> (có sẵn) hoặc <c>cloned</c> (của site này).</param>
/// <param name="PreviewUrl">Link nghe thử — của engine, hoặc link ký sẵn tới mẫu ghi âm đã tải lên (hết hạn sau ít phút).</param>
public record AdVideoVoiceDto(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    string? PreviewUrl,
    DateTime CreatedAt)
{
    public bool IsCloned => Kind == "cloned";
}

/// <summary>Một file ghi âm mẫu để clone giọng.</summary>
public sealed record AdVideoVoiceSample(string FileName, Func<Stream> Open);

/// <summary>
/// Clone giọng. <see cref="ConsentStatement"/> lưu nguyên văn bên AdVideo cùng người xác nhận và thời điểm —
/// là bằng chứng chủ giọng đã đồng ý.
/// </summary>
public record CloneVoiceInput(
    string Name,
    string? Description,
    string ConsentStatement,
    bool ConsentConfirmed,
    string ConsentedBy,
    IReadOnlyList<AdVideoVoiceSample> Samples);

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
/// <param name="VoiceId">Giọng đã chọn (id từ danh sách giọng); null = giọng mặc định.</param>
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
    IReadOnlyList<Guid> ProductImageIds,
    Guid? VoiceId = null);
