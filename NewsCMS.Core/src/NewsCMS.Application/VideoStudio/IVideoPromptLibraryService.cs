using NewsCMS.Application.Common;

namespace NewsCMS.Application.VideoStudio;

/// <param name="Source"><c>default</c>, <c>manual</c> hoặc <c>trend</c>.</param>
/// <param name="Status"><c>published</c>, <c>pending_review</c> hoặc <c>hidden</c>.</param>
public record VideoPromptTemplateDto(
    Guid Id,
    string Title,
    string Category,
    string? Description,
    string ScenePrompt,
    string ScriptTemplate,
    string AspectRatio,
    int DurationSeconds,
    bool HasPerson,
    string Source,
    string Status,
    string? TrendName,
    IReadOnlyList<string> SourceUrls,
    DateTime? ExpiresAt,
    int UsageCount,
    int SortOrder,
    DateTime CreatedAt)
{
    public bool IsTrend => Source == "trend";

    public bool IsExpired => ExpiresAt is { } at && at <= DateTime.UtcNow;
}

public record VideoPromptTemplateInput(
    string? Title,
    string? Category,
    string? Description,
    string? ScenePrompt,
    string? ScriptTemplate,
    string? AspectRatio,
    int DurationSeconds,
    bool HasPerson,
    int SortOrder);

public record VideoPromptLibrarySettingsDto(
    bool TrendAutoUpdateEnabled,
    int TrendIntervalHours,
    int TemplatesPerRun,
    int TrendLifetimeDays,
    bool RequireReview,
    string? Focus);

public record VideoPromptTrendRunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int Added,
    int Rejected,
    int Expired,
    bool UsedWebSearch,
    string? Notes);

/// <summary>
/// Kho mẫu brief cho form tạo video: mẫu mặc định, mẫu quản trị viết, mẫu tự cập nhật theo xu hướng.
/// </summary>
public interface IVideoPromptLibraryService
{
    /// <summary>Mẫu người tạo video chọn được: đang hiện và chưa hết hạn. Mẫu trend mới nhất lên đầu.</summary>
    Task<IReadOnlyList<VideoPromptTemplateDto>> GetPublishedAsync(CancellationToken ct = default);

    /// <param name="source">Lọc theo nguồn (<c>default</c>/<c>manual</c>/<c>trend</c>), null = tất cả.</param>
    /// <param name="status">Lọc theo trạng thái, null = tất cả.</param>
    Task<IReadOnlyList<VideoPromptTemplateDto>> GetAllAsync(string? source = null, string? status = null, CancellationToken ct = default);

    Task<VideoPromptTemplateDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<VideoPromptTemplateDto>> CreateAsync(VideoPromptTemplateInput input, CancellationToken ct = default);

    Task<Result<VideoPromptTemplateDto>> UpdateAsync(Guid id, VideoPromptTemplateInput input, CancellationToken ct = default);

    /// <summary>Hiện / ẩn / duyệt. Duyệt mẫu trend = chuyển sang <c>published</c>.</summary>
    Task<Result> SetStatusAsync(Guid id, string status, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Một video vừa tạo từ mẫu này. Không lỗi nếu mẫu đã bị xoá.</summary>
    Task RecordUsageAsync(Guid id, CancellationToken ct = default);

    Task<VideoPromptLibrarySettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<Result> SaveSettingsAsync(VideoPromptLibrarySettingsDto input, CancellationToken ct = default);

    Task<IReadOnlyList<VideoPromptTrendRunDto>> GetRunsAsync(int take = 10, CancellationToken ct = default);
}

/// <summary>Cập nhật kho mẫu theo xu hướng bằng AI (+ công cụ tìm web nếu có).</summary>
public interface IVideoPromptTrendService
{
    /// <summary>
    /// Chạy một lần: ẩn mẫu trend hết hạn, xin AI mẫu mới, kiểm tra, lưu. Hai lần chạy không bao giờ chồng nhau.
    /// </summary>
    /// <param name="trigger"><c>schedule</c> hoặc tên người bấm "Cập nhật ngay".</param>
    Task<Result<VideoPromptTrendRunDto>> RefreshAsync(string trigger, CancellationToken ct = default);

    /// <summary>Đến lịch chạy chưa (đã bật và lần chạy gần nhất đủ cũ).</summary>
    Task<bool> IsDueAsync(CancellationToken ct = default);
}
