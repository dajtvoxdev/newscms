using AdVideo.Core.Enums;
using AdVideo.Core.Timeline;

namespace AdVideo.Core.Pipeline;

/// <summary>Kết quả một bước. Không dùng exception cho lỗi nghiệp vụ — exception chỉ dành cho lỗi hạ tầng.</summary>
public sealed record StepResult(bool IsSuccess, string? FailureReason = null, string? RawError = null, bool IsRetryable = false)
{
    public static StepResult Ok() => new(true);

    /// <summary>Lỗi không retry được: nội dung bị từ chối, thiếu dữ liệu, vượt trần chi tiêu.</summary>
    public static StepResult Fail(string reason, string? rawError = null) =>
        new(false, reason, rawError, IsRetryable: false);

    /// <summary>Lỗi hạ tầng tạm thời — worker có thể chạy lại bước này.</summary>
    public static StepResult Retry(string reason, string? rawError = null) =>
        new(false, reason, rawError, IsRetryable: true);
}

/// <summary>
/// Trạng thái dùng chung giữa các bước. Mutable có chủ đích.
/// </summary>
/// <remarks>
/// Pipeline là một chuỗi bước nối tiếp, mỗi bước làm giàu thêm cùng một ngữ cảnh. Biến nó thành
/// bất biến sẽ buộc mỗi bước trả về một bản sao lớn — nhiều code hơn mà không an toàn hơn, vì
/// các bước vốn chạy tuần tự trong một worker chứ không song song.
///
/// <b>Chỉ worker chạm vào object này.</b> API không dùng nó.
/// </remarks>
public sealed class PipelineContext
{
    public required Guid JobId { get; init; }
    public required Guid TenantId { get; init; }

    /// <summary>Provider đã chọn cho toàn job (Luật 1). Đặt ở bước chọn provider, mọi bước sau đọc.</summary>
    public string? ProviderName { get; set; }
    public string? ProviderModelId { get; set; }

    public VideoTier Tier { get; set; } = VideoTier.Standard;
    public AspectRatio AspectRatio { get; set; } = AspectRatio.Portrait9x16;
    public int TargetDurationSeconds { get; set; }
    public bool HasPerson { get; set; }

    /// <summary>Brief gốc, đã parse. Sprint 1 người gọi tự viết lời thoại nên chỉ cần phần lời.</summary>
    public string? NarrationText { get; set; }

    public Guid? VoiceProfileId { get; set; }
    public string? VoiceId { get; set; }
    public string? TtsProviderName { get; set; }

    /// <summary>Object key của ảnh sản phẩm đã ingest (bước 1).</summary>
    public List<string> ProductImageKeys { get; } = [];

    /// <summary>Audio giọng đọc: object key + độ dài thật + mốc thời gian.</summary>
    public string? VoiceAudioKey { get; set; }
    public double VoiceDurationSeconds { get; set; }
    public List<Providers.WordTiming> WordTimings { get; } = [];
    public string? TtsRequestId { get; set; }

    /// <summary>Timeline đã khoá (bước 5). Bất biến từ đây — bước 6 chỉ đọc.</summary>
    public TimelinePlan? Timeline { get; set; }

    /// <summary>Object key clip của từng shot, theo chỉ số shot.</summary>
    public Dictionary<int, string> ShotClipKeys { get; } = [];

    /// <summary>Track tiếng gốc của từng shot, nếu provider sinh ra và mình quyết định giữ lại.</summary>
    public Dictionary<int, string> ShotNativeAudioKeys { get; } = [];

    /// <summary>Video cuối sau bước 8.</summary>
    public string? FinalVideoKey { get; set; }
    public double FinalDurationSeconds { get; set; }
    public string? ThumbnailKey { get; set; }
    public string? SubtitleKey { get; set; }

    /// <summary>Chi phí cộng dồn trong job. So với trần để dừng trước khi tiêu quá (R2).</summary>
    public decimal AccumulatedCostUsd { get; set; }
    public decimal? MaxCostUsd { get; set; }

    /// <summary>Đã vượt trần chi tiêu. Bước nào thấy cờ này cũng phải dừng ngay.</summary>
    public bool IsCostCeilingHit => MaxCostUsd is { } ceiling && AccumulatedCostUsd >= ceiling;

    /// <summary>
    /// Dữ liệu tuỳ ý bước này để lại cho bước sau.
    /// </summary>
    /// <remarks>
    /// Có sẵn để thêm một bước mới không phải sửa class này — nhưng đừng biến nó thành thùng
    /// chứa mọi thứ: dữ liệu mà HAI bước cùng cần thì nên là thuộc tính tường minh.
    /// </remarks>
    public Dictionary<string, object?> Bag { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Một bước trong pipeline 9 bước. Số thứ tự CỐ Ý trùng với tài liệu thiết kế.
/// </summary>
/// <remarks>
/// Thứ tự <b>4 → 5 → 6 không đảo được</b>: có audio thật trước, rồi mới khoá timeline theo audio,
/// rồi mới render hình theo timeline. Làm ngược thì hình 8 giây mà tiếng 9,3 giây, và không có
/// cách sửa nào không xấu. <see cref="Order"/> là chỗ mã hoá ràng buộc đó.
/// </remarks>
public interface IPipelineStep
{
    /// <summary>Số thứ tự trong pipeline 9 bước: 1 ingest · 4 TTS · 5 khoá timeline · 6 render · 8 compose · 9 QC.</summary>
    int Order { get; }

    /// <summary>Trạng thái job khi bước này đang chạy. Worker ghi vào DB để UI hiện tiến độ.</summary>
    JobStatus RunningStatus { get; }

    /// <summary>Tên bước bằng tiếng Việt, cho người dùng. Ví dụ "Đang thu giọng đọc".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Bước này có chạy không. Sprint 1 bỏ qua bước 2, 3, 7 nên các step đó trả false.
    /// </summary>
    /// <remarks>
    /// Quyết định ở đây chứ không phải bằng cách không đăng ký step vào DI: giữ step đã viết
    /// nhưng tắt bằng cờ thì Sprint 2 chỉ việc bật lên, không phải viết lại.
    /// </remarks>
    bool ShouldRun(PipelineContext context);

    Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
