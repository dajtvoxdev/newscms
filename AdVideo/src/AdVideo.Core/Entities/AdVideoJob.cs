using AdVideo.Core.Common;
using AdVideo.Core.Enums;

namespace AdVideo.Core.Entities;

/// <summary>
/// Bảng trung tâm. Một job = một video khách yêu cầu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provider nằm ở JOB, không phải ở <see cref="Shot"/>.</b> Đây là Luật 1: mỗi model có
/// "look" riêng — tông màu, độ tương phản, cách xử lý chuyển động, kết cấu da. Ghép một shot
/// Veo với một shot Seedance là nhìn ra ngay hai đoạn dán lại. Nên một job chỉ dùng một provider;
/// provider chết thì job chết, mời khách render lại, không tự đổi giữa chừng.
/// </para>
/// <para>
/// <b>Hai con số chi phí, không phải một</b> (D8): <see cref="EstimatedCostUsd"/> là dự toán
/// lúc nhận job để chặn trước khi tiêu; <see cref="ActualCostUsd"/> là tổng thật từ
/// <see cref="ProviderCall"/>. Hai số này lệch nhau là tín hiệu cần xem, không phải để "sửa cho khớp".
/// </para>
/// </remarks>
public class AdVideoJob : AuditableEntity, ITenantScoped, ISoftDelete
{
    public required Guid TenantId { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Khoá chống trùng. Bắt buộc trên mọi request tạo job.
    /// </summary>
    /// <remarks>
    /// Unique index theo <c>(TenantId, IdempotencyKey)</c>. Người dùng bấm hai lần, F5 giữa
    /// chừng, hoặc client retry vì timeout — tất cả phải cho ra MỘT job. Không có cái này thì
    /// một lần mạng chập có thể nhân đôi hoá đơn.
    /// </remarks>
    public required string IdempotencyKey { get; set; }

    /// <summary>
    /// SHA-256 của payload request, để phân biệt "gửi lại cùng request" với "tái dùng key cho request khác".
    /// </summary>
    /// <remarks>
    /// Không có cột này thì khi key trùng, hệ thống chỉ có hai lựa chọn đều sai: tạo job mới
    /// (mất tác dụng chống trùng, có thể nhân đôi hoá đơn) hoặc trả job cũ (khách nhận video
    /// sai brief mà không được báo). Xem <see cref="Core.Idempotency.IdempotencyGuard"/>.
    /// </remarks>
    public string? RequestHash { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Queued;

    /// <summary>Bước pipeline hiện tại (1–9). Trùng số với <see cref="JobStatus"/> đang chạy.</summary>
    public int CurrentStep { get; set; }

    public VideoTier Tier { get; set; } = VideoTier.Standard;

    public AspectRatio AspectRatio { get; set; } = AspectRatio.Portrait9x16;

    public int TargetDurationSeconds { get; set; } = 8;

    /// <summary>Brief gốc của khách, lưu JSON nguyên văn.</summary>
    /// <remarks>
    /// Giữ nguyên văn kể cả khi đã được lớp đạo diễn biến đổi: khi khách khiếu nại nội dung
    /// quảng cáo (R3) thì phải chứng minh được họ đã gửi gì.
    /// </remarks>
    public required string BriefJson { get; set; }

    /// <summary>Mã định dạng. Nullable ở Sprint 1 (chưa có LLM đạo diễn).</summary>
    public string? FormatCode { get; set; }

    /// <summary>
    /// Provider đã chọn cho TOÀN BỘ job. Xem <see cref="Shot"/> — không có provider riêng từng shot.
    /// </summary>
    public string? Provider { get; set; }

    public string? ProviderModelId { get; set; }

    public Guid? VoiceProfileId { get; set; }

    /// <summary>Job có dừng chờ duyệt storyboard không (bước 3, Sprint 2).</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>Video có người xuất hiện không. Cùng với <see cref="Tier"/> là hai thứ khách chọn (Luật 3).</summary>
    public bool HasPerson { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Tổng chi phí thật, cộng từ <see cref="ProviderCall"/>. Là nguồn sự thật, không phải credit.</summary>
    public decimal ActualCostUsd { get; set; }

    /// <summary>Trần chi tiêu cho job này. Vượt thì dừng — hàng rào chống thủng ví (R2).</summary>
    public decimal? MaxCostUsd { get; set; }

    /// <summary>Số lần render lại shot. Dữ liệu cho Sprint 6 tính giá vốn thật.</summary>
    public int RegenerateCount { get; set; }

    /// <summary>Lý do fail, viết cho người dùng đọc.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Lỗi nguyên văn từ provider. Chỉ để chẩn đoán, không trả ra API.</summary>
    public string? RawProviderError { get; set; }

    public DateTime? QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Guid? FinalVideoAssetId { get; set; }
    public Guid? ThumbnailAssetId { get; set; }
    public Guid? SubtitleAssetId { get; set; }
    public Guid? VoiceAudioAssetId { get; set; }

    /// <summary>Bằng chứng khách đã chấp nhận điều khoản ở job này, không phải một lần lúc đăng ký.</summary>
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedByIp { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public AdVideoProject? Project { get; set; }
    public ICollection<Shot> Shots { get; set; } = [];
    public ICollection<MediaAsset> Assets { get; set; } = [];
    public ICollection<ProviderCall> ProviderCalls { get; set; } = [];
}
