using AdVideo.Core.Entities;
using AdVideo.Core.Enums;

namespace AdVideo.Api.Contracts;

/// <summary>Phản hồi của <c>POST /v1/ad-videos</c> (202) và <c>GET /v1/ad-videos/{id}</c>.</summary>
/// <remarks>
/// <para>
/// Thiết kế ghi phản hồi có <c>credits_held</c> và <c>credits_balance_after_hold</c>. Hệ thống
/// credit bị <b>hoãn</b> có chủ đích, nên ở đây là đô la thật. Trả về một trường credit giả lập
/// để "giữ đúng hợp đồng" sẽ khiến bên tích hợp dựng UI quanh một con số không có sổ sách nào
/// đứng sau.
/// </para>
/// <para>
/// <b>Không trả <c>RawProviderError</c> ra ngoài.</b> Lỗi nguyên văn của provider có thể chứa
/// endpoint, tên model, đôi khi cả một phần payload. <see cref="FailureReason"/> là bản viết cho
/// khách đọc; bản nguyên văn chỉ nằm trong DB và log.
/// </para>
/// </remarks>
public sealed record AdVideoJobResponse
{
    public required string JobId { get; init; }

    /// <summary>Trạng thái dạng chuỗi snake_case: <c>queued</c>, <c>rendering_shots</c>, <c>completed</c>…</summary>
    public required string Status { get; init; }

    /// <summary>Bước hiện tại trong pipeline 9 bước.</summary>
    public int CurrentStep { get; init; }

    /// <summary>Tên bước bằng tiếng Việt, để UI hiện thẳng.</summary>
    public string? StepName { get; init; }

    /// <summary>Tiến độ 0–100, suy ra từ bước. Không phải phần trăm byte đã xử lý.</summary>
    public int ProgressPercent { get; init; }

    public string? Quality { get; init; }
    public string? AspectRatio { get; init; }
    public int DurationSeconds { get; init; }

    /// <summary>Provider đã chọn. Hiện ra để đối soát hoá đơn, không phải để khách chọn lại.</summary>
    public string? Provider { get; init; }

    public decimal EstimatedCostUsd { get; init; }
    public decimal ActualCostUsd { get; init; }

    /// <summary>Lý do thất bại, viết cho người đọc.</summary>
    public string? FailureReason { get; init; }

    /// <summary>URL tải video, chỉ có khi job xong. URL có hạn — xem <c>DownloadUrlLifetimeMinutes</c>.</summary>
    public string? DownloadUrl { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? EstimatedReadyAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    public static AdVideoJobResponse From(AdVideoJob job, string? downloadUrl = null, DateTime? estimatedReadyAt = null) =>
        new()
        {
            JobId = job.Id.ToString(),
            Status = ToApiStatus(job.Status),
            CurrentStep = job.CurrentStep,
            StepName = StepDisplayName(job.Status),
            ProgressPercent = ProgressOf(job.Status),
            Quality = job.Tier.ToString().ToLowerInvariant(),
            AspectRatio = job.AspectRatio.ToProviderString(),
            DurationSeconds = job.TargetDurationSeconds,
            Provider = job.Provider,
            EstimatedCostUsd = job.EstimatedCostUsd,
            ActualCostUsd = job.ActualCostUsd,
            FailureReason = job.FailureReason,
            DownloadUrl = downloadUrl,
            CreatedAt = job.CreatedAt,
            EstimatedReadyAt = estimatedReadyAt,
            CompletedAt = job.CompletedAt,
        };

    /// <summary>Tên trạng thái trong API. Cố định bằng tay chứ không sinh từ enum.</summary>
    /// <remarks>
    /// Sinh tự động từ tên enum nghĩa là đổi tên một hằng số C# sẽ lặng lẽ đổi hợp đồng công khai
    /// và làm hỏng client của khách. Bảng này là ranh giới giữa hai thứ đó.
    /// </remarks>
    public static string ToApiStatus(JobStatus status) => status switch
    {
        JobStatus.Queued => "queued",
        JobStatus.Ingesting => "ingesting",
        JobStatus.Directing => "directing",
        JobStatus.AwaitingApproval => "awaiting_approval",
        JobStatus.SynthesizingVoice => "synthesizing_voice",
        JobStatus.LockingTimeline => "locking_timeline",
        JobStatus.RenderingShots => "rendering_shots",
        JobStatus.LipSyncing => "lip_syncing",
        JobStatus.Composing => "composing",
        JobStatus.QualityChecking => "quality_checking",
        JobStatus.Completed => "completed",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "cancelled",
        _ => "unknown",
    };

    private static string? StepDisplayName(JobStatus status) => status switch
    {
        JobStatus.Queued => "Đang chờ tới lượt",
        JobStatus.Ingesting => "Đang nạp ảnh và kiểm tra đầu vào",
        JobStatus.Directing => "Đang dựng kịch bản",
        JobStatus.AwaitingApproval => "Đang chờ bạn duyệt kịch bản",
        JobStatus.SynthesizingVoice => "Đang thu giọng đọc",
        JobStatus.LockingTimeline => "Đang khớp thời lượng theo giọng đọc",
        JobStatus.RenderingShots => "Đang dựng hình",
        JobStatus.LipSyncing => "Đang khớp khẩu hình",
        JobStatus.Composing => "Đang ghép video",
        JobStatus.QualityChecking => "Đang kiểm tra chất lượng",
        JobStatus.Completed => "Đã xong",
        JobStatus.Failed => "Đã dừng vì lỗi",
        JobStatus.Cancelled => "Đã huỷ",
        _ => null,
    };

    /// <summary>
    /// Tiến độ theo bước, có trọng số.
    /// </summary>
    /// <remarks>
    /// Không chia đều 9 bước: render hình (bước 6) chiếm phần lớn thời gian thật, nên chia đều sẽ
    /// tạo ra một thanh tiến độ chạy vèo tới 55% rồi đứng im nhiều phút. Các mốc dưới đây bám theo
    /// thời lượng thật của từng bước.
    /// </remarks>
    private static int ProgressOf(JobStatus status) => status switch
    {
        JobStatus.Queued => 0,
        JobStatus.Ingesting => 5,
        JobStatus.Directing => 10,
        JobStatus.AwaitingApproval => 15,
        JobStatus.SynthesizingVoice => 20,
        JobStatus.LockingTimeline => 25,
        JobStatus.RenderingShots => 40,
        JobStatus.LipSyncing => 75,
        JobStatus.Composing => 85,
        JobStatus.QualityChecking => 95,
        JobStatus.Completed => 100,

        // Thất bại và huỷ giữ nguyên tiến độ 0: một thanh tiến độ 100% màu đỏ là thứ gây hiểu nhầm
        // nhiều hơn là thông tin.
        _ => 0,
    };
}

/// <summary>Phản hồi <c>GET /healthz</c>.</summary>
public sealed record HealthResponse(string Status, IDictionary<string, string> Checks);
