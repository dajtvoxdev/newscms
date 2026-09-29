using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio;

// ── Cấu hình model (SuperAdmin) ─────────────────────────────────────────────

public sealed record ImageModelDto(
    Guid Id,
    string Name,
    string? Description,
    Guid ConnectionId,
    string? ConnectionName,
    ImageProviderAdapter Adapter,
    string ModelId,
    ImageCapabilities Capabilities,
    int MaxReferenceImages,
    MaskConvention MaskConvention,
    string SupportedSizes,
    int MaxVariants,
    string? Quality,
    string OutputFormat,
    decimal PricePerImageUsd,
    int TimeoutSeconds,
    string? ExtraParamsJson,
    bool IsActive,
    bool IsDefault,
    int SortOrder,
    bool AdapterAvailable,
    DateTime? LastTestedAt,
    bool? LastTestOk,
    string? LastTestError);

public sealed record ImageModelUpsertDto(
    Guid? Id,
    string Name,
    string? Description,
    Guid ConnectionId,
    ImageProviderAdapter Adapter,
    string ModelId,
    ImageCapabilities Capabilities,
    int MaxReferenceImages,
    MaskConvention MaskConvention,
    string SupportedSizes,
    int MaxVariants,
    string? Quality,
    string OutputFormat,
    decimal PricePerImageUsd,
    int TimeoutSeconds,
    string? ExtraParamsJson,
    bool IsActive,
    bool IsDefault,
    int SortOrder);

/// <summary>Kết quả "Chạy thử" model: ảnh xem trước (không vào thư viện) hoặc lỗi.</summary>
public sealed record ImageModelTestResultDto(bool Ok, string? PreviewUrl, string? Error, int DurationMs);

/// <summary>Kết nối AI chọn được cho model ảnh (key nằm trong kết nối).</summary>
public sealed record ImageConnectionOptionDto(Guid Id, string Name, string BaseUrl, bool HasApiKey);

// ── Form tạo ảnh (người dùng) ───────────────────────────────────────────────

/// <summary>Một model người dùng chọn được — không lộ connection, model id hay tham số kỹ thuật.</summary>
public sealed record ImageModelOptionDto(
    Guid Id,
    string Name,
    string? Description,
    decimal PricePerImageUsd,
    int MaxVariants,
    ImageCapabilities Capabilities,
    bool IsDefault);

public sealed record ImageStudioFormDto(
    bool Enabled,
    string? DisabledReason,
    IReadOnlyList<ImageModelOptionDto> Models,
    IReadOnlyList<string> AspectRatios,
    int? MonthlyRemaining,
    int? DailyRemaining,
    string CoverAspect,
    string ProductAspect);

public sealed record ImageJobCreateInput(
    string IdempotencyKey,
    Guid ModelId,
    string Prompt,
    string AspectRatio,
    int VariantCount,
    ImagePurpose Purpose = ImagePurpose.Free,
    Guid? TemplateId = null,
    string? ContextType = null,
    Guid? ContextId = null);

public sealed record ImageJobOutputDto(
    Guid Id,
    int Index,
    string? Url,
    int Width,
    int Height,
    long Bytes,
    Guid? PromotedMediaId,
    bool IsPurged);

public sealed record ImageJobDto(
    Guid Id,
    ImageJobStatus Status,
    ImageJobMode Mode,
    ImagePurpose Purpose,
    string ModelName,
    string UserPrompt,
    string FinalPrompt,
    string AspectRatio,
    string Size,
    int VariantCount,
    decimal EstimatedCostUsd,
    decimal CostUsd,
    string? Error,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    Guid? CreatedBy,
    IReadOnlyList<ImageJobOutputDto> Outputs)
{
    public bool IsFinished => Status is ImageJobStatus.Succeeded or ImageJobStatus.Failed or ImageJobStatus.Canceled;
}

public sealed record ImageJobListItemDto(
    Guid Id,
    ImageJobStatus Status,
    ImagePurpose Purpose,
    string ModelName,
    string UserPrompt,
    int VariantCount,
    int OutputCount,
    decimal CostUsd,
    DateTime QueuedAt,
    string? ThumbnailUrl);

public sealed record ImagePromoteResultDto(Guid MediaId, string Url, string? AltText, int? Width, int? Height);

// ── Bật site + hạn mức (SuperAdmin) ─────────────────────────────────────────

public sealed record ImageStudioSiteRowDto(
    Guid SiteId,
    string SiteName,
    string SiteSlug,
    bool Enabled,
    int MonthlyImageQuota,
    int PerUserDailyQuota,
    int ImagesThisMonth,
    decimal CostThisMonthUsd);

public sealed record ImageStudioSiteQuotaInput(Guid SiteId, bool Enabled, int MonthlyImageQuota, int PerUserDailyQuota);
