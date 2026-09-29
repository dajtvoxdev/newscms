using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.ImageStudio;

public enum ImagePromptTemplateSource
{
    /// <summary>Mẫu mặc định đi kèm hệ thống.</summary>
    Default = 0,

    /// <summary>Quản trị viên tự viết.</summary>
    Manual = 1,

    /// <summary>Tác vụ cập nhật theo trend sinh ra. Có hạn dùng.</summary>
    Trend = 2,
}

public enum ImagePromptTemplateStatus
{
    /// <summary>Người tạo ảnh thấy và chọn được.</summary>
    Published = 0,

    /// <summary>Mẫu trend chờ quản trị duyệt.</summary>
    PendingReview = 1,

    /// <summary>Ẩn khỏi modal tạo ảnh, vẫn giữ trong kho.</summary>
    Hidden = 2,
}

/// <summary>Mẫu sửa ảnh gợi ý người dùng khoanh vùng thế nào (Đợt 3).</summary>
public enum ImageRegionHint
{
    None = 0,

    /// <summary>Khoanh chủ thể (sản phẩm) làm vùng Giữ nguyên, đổi phần còn lại.</summary>
    KeepSubject = 1,

    /// <summary>Khoanh các vùng cần sửa.</summary>
    EditRegions = 2,
}

/// <summary>
/// Một mẫu prompt tạo ảnh. Dùng chung mọi site. Người dùng chọn mẫu trong modal rồi sửa tiếp — mẫu chỉ
/// điền sẵn, không khoá gì.
/// </summary>
/// <remarks>
/// <para>
/// Chỗ giữ trong <see cref="Prompt"/>: <c>{chu_de}</c>, <c>{san_pham}</c>, <c>{mo_ta}</c>,
/// <c>{thuong_hieu}</c> — modal điền từ ngữ cảnh (bài viết, sản phẩm) hoặc cho người dùng gõ.
/// </para>
/// <para>
/// Ảnh demo không phải <c>Media</c> (thư viện media theo site, còn mẫu dùng chung): file nằm ở storage
/// <c>ai-images/demos/…</c>, trỏ bằng <see cref="DemoStorageKey"/>.
/// </para>
/// </remarks>
public class ImagePromptTemplate : AuditableEntity, ISoftDelete
{
    public const string TopicPlaceholder = "{chu_de}";
    public const string ProductPlaceholder = "{san_pham}";
    public const string DescriptionPlaceholder = "{mo_ta}";
    public const string BrandPlaceholder = "{thuong_hieu}";

    public string Title { get; set; } = default!;

    /// <summary>Ngành / nhóm để lọc, ví dụ "Ăn uống", "Tin tức & bài viết".</summary>
    public string Category { get; set; } = default!;

    /// <summary>Mẫu dành cho chỗ nào: ảnh bìa, ảnh sản phẩm… Modal mở từ chỗ nào thì ưu tiên mẫu của chỗ đó.</summary>
    public ImagePurpose Purpose { get; set; }

    /// <summary>Một câu: mẫu này cho ra ảnh thế nào, hợp với nội dung gì.</summary>
    public string? Description { get; set; }

    public string Prompt { get; set; } = default!;

    public string AspectRatio { get; set; } = "1:1";

    /// <summary>Mẫu sửa ảnh — cần ảnh gốc (Đợt 3). Modal Đợt 2 chưa hiện các mẫu này.</summary>
    public bool RequiresSourceImage { get; set; }

    public ImageRegionHint RegionHint { get; set; }

    public ImagePromptTemplateSource Source { get; set; }

    public ImagePromptTemplateStatus Status { get; set; }

    public string? TrendName { get; set; }

    /// <summary>Link nguồn xu hướng, mỗi dòng một link.</summary>
    public string? SourceUrls { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Guid? TrendRunId { get; set; }

    public int UsageCount { get; set; }

    public int SortOrder { get; set; }

    // ── Ảnh demo ──────────────────────────────────────────────────────────────
    public string? DemoStorageKey { get; set; }

    public string? DemoImageUrl { get; set; }

    public DateTime? DemoUpdatedAt { get; set; }

    /// <summary>"Tạo bằng GPT Image" hoặc "Tải lên" — để quản trị biết ảnh demo từ đâu ra.</summary>
    public string? DemoSource { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}

public enum ImagePromptTrendRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>Một lần chạy cập nhật mẫu ảnh theo trend.</summary>
public class ImagePromptTrendRun : AuditableEntity
{
    /// <summary><c>schedule</c> (tự động) hoặc tên người bấm "Cập nhật ngay".</summary>
    public string Trigger { get; set; } = default!;

    public ImagePromptTrendRunStatus Status { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public int Added { get; set; }

    public int Rejected { get; set; }

    public int Expired { get; set; }

    /// <summary>Số ảnh demo tự tạo cho mẫu mới trong lần chạy này.</summary>
    public int DemosCreated { get; set; }

    public bool UsedWebSearch { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Cấu hình kho mẫu ảnh — MỘT dòng cho cả hệ thống, sửa ở màn hình quản trị.</summary>
public class ImagePromptLibrarySettings : AuditableEntity
{
    public bool TrendAutoUpdateEnabled { get; set; }

    public int TrendIntervalHours { get; set; } = 24;

    public int TemplatesPerRun { get; set; } = 6;

    public int TrendLifetimeDays { get; set; } = 14;

    public bool RequireReview { get; set; }

    public string? Focus { get; set; }

    /// <summary>
    /// Từ bị chặn trong mẫu (mỗi dòng một từ): nhân vật có bản quyền, phong cách của nghệ sĩ còn sống,
    /// thương hiệu… Áp cho cả mẫu tự viết lẫn mẫu trend.
    /// </summary>
    public string? BlockedTerms { get; set; }

    /// <summary>Lần chạy theo lịch tự tạo ảnh demo cho mẫu trend mới (tốn tiền mỗi ảnh).</summary>
    public bool AutoDemoForTrend { get; set; }

    /// <summary>Trần số ảnh demo tự tạo mỗi lần chạy.</summary>
    public int MaxAutoDemosPerRun { get; set; } = 6;

    /// <summary>Model dùng để tạo ảnh demo. Null = model mặc định.</summary>
    public Guid? DemoModelId { get; set; }
}
