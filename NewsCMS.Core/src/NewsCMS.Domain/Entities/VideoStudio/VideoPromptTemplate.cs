using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.VideoStudio;

/// <summary>Mẫu đến từ đâu.</summary>
public enum VideoPromptTemplateSource
{
    /// <summary>Mẫu mặc định đi kèm hệ thống.</summary>
    Default = 0,

    /// <summary>Quản trị viên tự viết.</summary>
    Manual = 1,

    /// <summary>Tác vụ cập nhật theo trend sinh ra. Có hạn dùng.</summary>
    Trend = 2,
}

public enum VideoPromptTemplateStatus
{
    /// <summary>Người tạo video thấy và chọn được.</summary>
    Published = 0,

    /// <summary>Mẫu trend chờ quản trị duyệt (khi bật "duyệt trước khi hiện").</summary>
    PendingReview = 1,

    /// <summary>Ẩn khỏi form tạo video, vẫn giữ trong kho.</summary>
    Hidden = 2,
}

/// <summary>
/// Một mẫu brief cho video quảng cáo: cảnh quay, lời thoại, khung hình, thời lượng.
/// </summary>
/// <remarks>
/// <para>
/// Dùng chung cho mọi site — không site-scoped. Người làm marketing chọn mẫu ở form tạo video rồi sửa
/// tiếp; mẫu chỉ điền sẵn, không khoá gì.
/// </para>
/// <para>
/// Chỗ giữ tên sản phẩm viết là <c>{san_pham}</c> trong <see cref="ScenePrompt"/> và
/// <see cref="ScriptTemplate"/>; form thay bằng tên sản phẩm người dùng nhập.
/// </para>
/// </remarks>
public class VideoPromptTemplate : AuditableEntity, ISoftDelete
{
    public const string ProductPlaceholder = "{san_pham}";

    public string Title { get; set; } = default!;

    /// <summary>Ngành hàng, ví dụ "Ăn uống", "Mỹ phẩm". Dùng để lọc.</summary>
    public string Category { get; set; } = default!;

    /// <summary>Một câu: mẫu này hợp với sản phẩm nào, vì sao hiệu quả.</summary>
    public string? Description { get; set; }

    public string ScenePrompt { get; set; } = default!;

    public string ScriptTemplate { get; set; } = default!;

    /// <summary>9:16, 1:1 hoặc 16:9.</summary>
    public string AspectRatio { get; set; } = "9:16";

    public int DurationSeconds { get; set; } = 15;

    public bool HasPerson { get; set; }

    public VideoPromptTemplateSource Source { get; set; }

    public VideoPromptTemplateStatus Status { get; set; }

    /// <summary>Tên xu hướng mẫu trend dựa vào, ví dụ "POV mở hộp chậm".</summary>
    public string? TrendName { get; set; }

    /// <summary>Link nguồn xu hướng (mỗi dòng một link) — để quản trị kiểm tra lại.</summary>
    public string? SourceUrls { get; set; }

    /// <summary>Mẫu trend hết hạn thì tự ẩn khỏi form. Null = không hết hạn.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Lần chạy cập nhật trend đã sinh ra mẫu này.</summary>
    public Guid? TrendRunId { get; set; }

    /// <summary>Số video đã tạo từ mẫu — xếp mẫu hay dùng lên trước.</summary>
    public int UsageCount { get; set; }

    public int SortOrder { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum VideoPromptTrendRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>Một lần chạy cập nhật mẫu theo trend — để quản trị xem lần gần nhất ra sao.</summary>
public class VideoPromptTrendRun : AuditableEntity
{
    /// <summary><c>schedule</c> (tự động) hoặc tên người bấm "Cập nhật ngay".</summary>
    public string Trigger { get; set; } = default!;

    public VideoPromptTrendRunStatus Status { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public int Added { get; set; }

    public int Rejected { get; set; }

    public int Expired { get; set; }

    /// <summary>Có công cụ tìm web đang bật không. Không có thì trend chỉ dựa trên hiểu biết của model.</summary>
    public bool UsedWebSearch { get; set; }

    /// <summary>Lỗi, hoặc lý do từng mẫu bị loại (mỗi dòng một lý do).</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Cấu hình kho prompt — MỘT dòng cho cả hệ thống, sửa ở màn hình quản trị (không nằm trong appsettings).
/// </summary>
public class VideoPromptLibrarySettings : AuditableEntity
{
    /// <summary>Tự cập nhật mẫu theo trend theo lịch.</summary>
    public bool TrendAutoUpdateEnabled { get; set; }

    /// <summary>Bao lâu cập nhật một lần.</summary>
    public int TrendIntervalHours { get; set; } = 24;

    /// <summary>Số mẫu mới xin mỗi lần.</summary>
    public int TemplatesPerRun { get; set; } = 6;

    /// <summary>Mẫu trend sống bao nhiêu ngày rồi tự ẩn.</summary>
    public int TrendLifetimeDays { get; set; } = 14;

    /// <summary>Mẫu trend phải được quản trị duyệt trước khi người tạo video thấy.</summary>
    public bool RequireReview { get; set; }

    /// <summary>Thị trường / nền tảng / ngành cần tập trung, đưa vào prompt cho AI.</summary>
    public string? Focus { get; set; }
}
