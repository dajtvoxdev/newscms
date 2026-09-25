namespace AdVideo.Core.Enums;

/// <summary>
/// Vòng đời một job. Giá trị số của các trạng thái đang-chạy CỐ Ý trùng với số thứ tự
/// bước trong pipeline 9 bước (xem tài liệu thiết kế, mục Pipeline):
/// 1 nạp · 2 đạo diễn · 3 duyệt · 4 TTS · 5 khoá timeline · 6 render shot ·
/// 7 lip-sync · 8 compose · 9 QC.
/// </summary>
/// <remarks>
/// Nhờ trùng số nên so sánh thứ tự tiến triển bằng <c>&lt;</c> được:
/// <c>status &lt; JobStatus.RenderingShots</c> nghĩa là "chưa tới bước render".
/// Đừng chèn giá trị mới vào giữa — thêm vào cuối nhóm terminal.
///
/// Sprint 1 bỏ qua bước 2, 3, 7 nên <see cref="Directing"/>, <see cref="AwaitingApproval"/>,
/// <see cref="LipSyncing"/> sẽ không bao giờ xuất hiện cho tới Sprint 2/5.
/// </remarks>
public enum JobStatus
{
    /// <summary>Đã nhận, nằm trong hàng đợi, chưa worker nào cầm.</summary>
    Queued = 0,

    /// <summary>Bước 1 — tải ảnh, kiểm định dạng, resize, moderation.</summary>
    Ingesting = 1,

    /// <summary>Bước 2 — LLM đọc brief và sinh storyboard. (Sprint 2)</summary>
    Directing = 2,

    /// <summary>Bước 3 — job dừng chờ khách duyệt/sửa storyboard. (Sprint 2)</summary>
    AwaitingApproval = 3,

    /// <summary>Bước 4 — sinh giọng đọc tiếng Việt kèm mốc thời gian.</summary>
    SynthesizingVoice = 4,

    /// <summary>Bước 5 — gán thời lượng shot theo độ dài audio thật.</summary>
    LockingTimeline = 5,

    /// <summary>Bước 6 — gọi provider sinh video cho từng shot.</summary>
    RenderingShots = 6,

    /// <summary>Bước 7 — khớp môi, chỉ khi có người nói. (Sprint 5)</summary>
    LipSyncing = 7,

    /// <summary>Bước 8 — FFmpeg nối shot, trộn âm, vẽ nhãn AI, chuẩn hoá LUFS.</summary>
    Composing = 8,

    /// <summary>Bước 9 — QC tự động rồi đẩy MinIO, sinh thumbnail, bắn webhook.</summary>
    QualityChecking = 9,

    Completed = 100,
    Failed = 101,
    Cancelled = 102,
}

public static class JobStatusExtensions
{
    /// <summary>Trạng thái không thể tiến triển thêm. Job ở trạng thái này thì retry là tạo job mới.</summary>
    public static bool IsTerminal(this JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;

    /// <summary>Job đang chờ người, không phải đang chờ máy. UI phải hiển thị khác đi.</summary>
    public static bool NeedsHuman(this JobStatus status) => status == JobStatus.AwaitingApproval;
}
