using AdVideo.Core.Common;
using AdVideo.Core.Providers;

namespace AdVideo.Core.Entities;

/// <summary>
/// Mỗi lần gọi ra ngoài: provider, model, tham số, thời gian, chi phí, thành/bại.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bắt buộc có từ Sprint 1, không phải Sprint 5.</b> Không có bảng này thì không bao giờ
/// biết vì sao hoá đơn tăng, và dữ liệu quá khứ thì không dựng lại được. Khi ngồi thiết kế
/// gói bán ở Sprint 6, chi phí thật của vài trăm video là thứ quyết định hệ số — không có nó
/// thì lại đoán.
/// </para>
/// <para>
/// <b>Bảng này BẤT BIẾN.</b> Không soft-delete, không update nội dung sau khi ghi. Nó là bằng
/// chứng kiểm toán cho đối soát hoá đơn và cho khiếu nại. Chỉ append.
/// </para>
/// </remarks>
public class ProviderCall : BaseEntity
{
    public Guid? JobId { get; set; }

    /// <summary>Shot nào phát sinh lời gọi. Null cho lời gọi cấp job (TTS toàn bài, LLM đạo diễn).</summary>
    public int? ShotIndex { get; set; }

    public required Guid TenantId { get; set; }

    public required string Provider { get; set; }

    public required string ModelId { get; set; }

    public required ProviderCategory Category { get; set; }

    /// <summary>Request id phía provider. Là khoá để đối soát với hoá đơn của họ.</summary>
    public string? ProviderRequestId { get; init; }

    /// <summary>
    /// Tham số đã gửi, JSON — ĐÃ LOẠI KEY.
    /// </summary>
    /// <remarks>
    /// Ghi nguyên văn request là cần để tái hiện lỗi, nhưng request thường chứa API key trong
    /// header. Phải redact trước khi lưu, nếu không bảng này thành chỗ rò secret lớn nhất hệ thống.
    /// </remarks>
    public string? RequestJson { get; set; }

    public int? HttpStatus { get; set; }

    public bool IsSuccess { get; set; }

    public VideoFailureKind FailureKind { get; set; } = VideoFailureKind.None;

    /// <summary>Lỗi nguyên văn. Giữ để điền kết luận Sprint 0 và để chẩn đoán — không trả ra API.</summary>
    public string? RawError { get; set; }

    /// <summary>Thời gian lời gọi, mili giây. Để theo dõi xu hướng và phát hiện provider chậm dần.</summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Chi phí đô la. Provider nào không báo cost (fal.ai) thì tính từ giá trong capability.
    /// </summary>
    /// <remarks>
    /// Tổng cột này theo tháng phải khớp hoá đơn trong ±5% (tiêu chí S5). Lệch hơn nghĩa là
    /// đang bỏ sót một loại lời gọi — thường là retry, lip-sync, hoặc LLM đạo diễn.
    /// </remarks>
    public decimal CostUsd { get; set; }

    /// <summary>True nếu <see cref="CostUsd"/> do provider báo; false nếu mình tự suy ra từ đơn giá.</summary>
    public bool CostIsReported { get; set; }

    /// <summary>Số ký tự bị tính tiền (ElevenLabs tính theo ký tự, không theo giây audio).</summary>
    public int? BilledCharacterCount { get; set; }

    /// <summary>Lần thử thứ mấy. Retry mà không ghi số lần thì không bao giờ phát hiện được vòng lặp tốn tiền.</summary>
    public int AttemptNumber { get; set; } = 1;

    public AdVideoJob? Job { get; set; }
}
