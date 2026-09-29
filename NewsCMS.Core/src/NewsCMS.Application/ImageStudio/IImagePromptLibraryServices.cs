using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio;

/// <param name="Source"><c>default</c>, <c>manual</c> hoặc <c>trend</c>.</param>
/// <param name="Status"><c>published</c>, <c>pending_review</c> hoặc <c>hidden</c>.</param>
public sealed record ImagePromptTemplateDto(
    Guid Id,
    string Title,
    string Category,
    ImagePurpose Purpose,
    string? Description,
    string Prompt,
    string AspectRatio,
    bool RequiresSourceImage,
    ImageRegionHint RegionHint,
    string Source,
    string Status,
    string? TrendName,
    IReadOnlyList<string> SourceUrls,
    DateTime? ExpiresAt,
    int UsageCount,
    int SortOrder,
    DateTime CreatedAt,
    string? DemoImageUrl,
    string? DemoSource,
    DateTime? DemoUpdatedAt)
{
    public bool IsTrend => Source == "trend";

    public bool IsExpired => ExpiresAt is { } at && at <= DateTime.UtcNow;

    public string PurposeKey => ImagePurposes.Key(Purpose);

    public IReadOnlyList<string> Placeholders => ImagePromptPlaceholders.FindKnown(Prompt);
}

public sealed record ImagePromptTemplateInput(
    string? Title,
    string? Category,
    ImagePurpose Purpose,
    string? Description,
    string? Prompt,
    string? AspectRatio,
    int SortOrder,
    bool RequiresSourceImage = false,
    ImageRegionHint RegionHint = ImageRegionHint.None);

/// <summary>Mẫu đã lưu + cảnh báo không chặn (ví dụ yêu cầu chữ trong ảnh).</summary>
public sealed record ImagePromptTemplateSaved(ImagePromptTemplateDto Template, IReadOnlyList<string> Warnings);

public sealed record ImagePromptLibrarySettingsDto(
    bool TrendAutoUpdateEnabled,
    int TrendIntervalHours,
    int TemplatesPerRun,
    int TrendLifetimeDays,
    bool RequireReview,
    string? Focus,
    string? BlockedTerms,
    bool AutoDemoForTrend,
    int MaxAutoDemosPerRun,
    Guid? DemoModelId);

public sealed record ImagePromptTrendRunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int Added,
    int Rejected,
    int Expired,
    int DemosCreated,
    bool UsedWebSearch,
    string? Notes);

/// <summary>Ước tính trước khi bấm "Tạo demo cho mọi mẫu chưa có".</summary>
public sealed record ImageDemoEstimateDto(int Count, decimal EstimatedCostUsd, string? ModelName, string? Error);

public sealed record ImageDemoBatchResultDto(int Created, int Failed, string? Notes);

public sealed record ImagePromptSuggestInput(ImagePurpose Purpose, string? Title, string? Excerpt);

public sealed record ImagePromptSuggestionDto(string Prompt, string? Alt, string? Caption);

/// <summary>Kho mẫu prompt ảnh: mẫu mặc định, mẫu quản trị viết, mẫu tự cập nhật theo xu hướng.</summary>
public interface IImagePromptLibraryService
{
    /// <summary>
    /// Mẫu người tạo ảnh chọn được: đang hiện, chưa hết hạn, không cần ảnh gốc. Có
    /// <paramref name="purpose"/> thì chỉ lấy mẫu của mục đích đó và mẫu tự do. Trend mới nhất lên đầu.
    /// </summary>
    Task<IReadOnlyList<ImagePromptTemplateDto>> GetPublishedAsync(ImagePurpose? purpose = null, CancellationToken ct = default);

    Task<IReadOnlyList<ImagePromptTemplateDto>> GetAllAsync(string? source = null, string? status = null, CancellationToken ct = default);

    Task<ImagePromptTemplateDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<ImagePromptTemplateSaved>> CreateAsync(ImagePromptTemplateInput input, CancellationToken ct = default);

    Task<Result<ImagePromptTemplateSaved>> UpdateAsync(Guid id, ImagePromptTemplateInput input, CancellationToken ct = default);

    /// <summary>Hiện / ẩn / duyệt. Duyệt mẫu trend = chuyển sang <c>published</c>.</summary>
    Task<Result> SetStatusAsync(Guid id, string status, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Một lần tạo ảnh từ mẫu này. Không lỗi nếu mẫu đã bị xoá.</summary>
    Task RecordUsageAsync(Guid id, CancellationToken ct = default);

    Task<ImagePromptLibrarySettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<Result> SaveSettingsAsync(ImagePromptLibrarySettingsDto input, CancellationToken ct = default);

    Task<IReadOnlyList<ImagePromptTrendRunDto>> GetRunsAsync(int take = 10, CancellationToken ct = default);
}

/// <summary>Cập nhật kho mẫu ảnh theo xu hướng bằng AI (+ công cụ tìm web nếu có).</summary>
public interface IImagePromptTrendService
{
    /// <param name="trigger"><c>schedule</c> hoặc tên người bấm "Cập nhật ngay". Chỉ lần chạy theo lịch mới tự tạo ảnh demo.</param>
    Task<Result<ImagePromptTrendRunDto>> RefreshAsync(string trigger, CancellationToken ct = default);

    Task<bool> IsDueAsync(CancellationToken ct = default);
}

/// <summary>Ảnh demo cho mẫu: tạo bằng model, tải lên, xoá.</summary>
public interface IImagePromptDemoService
{
    /// <summary>Chạy mẫu với giá trị ví dụ cho chỗ giữ bằng model demo, một ảnh (tốn tiền, có ghi sổ).</summary>
    Task<Result<ImagePromptTemplateDto>> GenerateAsync(Guid templateId, CancellationToken ct = default);

    Task<Result<ImagePromptTemplateDto>> UploadAsync(Guid templateId, Stream content, long length, CancellationToken ct = default);

    Task<Result> RemoveAsync(Guid templateId, CancellationToken ct = default);

    Task<ImageDemoEstimateDto> EstimateMissingAsync(CancellationToken ct = default);

    /// <summary>Tạo demo cho các mẫu chưa có (đang hiện hoặc chờ duyệt), tối đa <paramref name="max"/> mẫu.</summary>
    Task<Result<ImageDemoBatchResultDto>> GenerateMissingAsync(int max, CancellationToken ct = default);
}

/// <summary>AI hỗ trợ viết prompt ảnh — dùng kết nối AI mặc định và skill quản trị sửa được.</summary>
public interface IImagePromptAssistant
{
    /// <summary>Viết lại mô tả cho chi tiết hơn, giữ ý và chỗ giữ.</summary>
    Task<Result<string>> EnhanceAsync(string prompt, ImagePurpose purpose, CancellationToken ct = default);

    /// <summary>Gợi ý prompt + alt + chú thích từ nội dung bài viết / sản phẩm.</summary>
    Task<Result<ImagePromptSuggestionDto>> SuggestAsync(ImagePromptSuggestInput input, CancellationToken ct = default);
}
