using AdVideo.Core.Common;
using AdVideo.Core.Enums;

namespace AdVideo.Core.Entities;

/// <summary>
/// Một cảnh trong video. Job dài = nhiều shot ghép lại (D5) vì không model nào sinh quá 15 giây một lần gọi.
/// </summary>
/// <remarks>
/// <b>Không có cột <c>Provider</c> ở đây.</b> Provider là thuộc tính của job (Luật 1). Cột
/// <see cref="ProviderModelId"/> chỉ ghi lại model THẬT SỰ đã render shot này để đối soát hoá
/// đơn và để phát hiện nếu có ai vô tình trộn provider — không phải để chọn provider theo shot.
///
/// Tách thành bảng riêng (thay vì nhét vào JSON trên job) là để endpoint regenerate một shot
/// cập nhật đúng một dòng, và để <c>RetryCount</c> cộng dồn được mà không đọc-ghi cả blob.
/// </remarks>
public class Shot : AuditableEntity
{
    public required Guid JobId { get; set; }

    /// <summary>Thứ tự trong job, 0-based. Cùng <see cref="JobId"/> tạo thành khoá duy nhất.</summary>
    public required int Index { get; set; }

    public ShotStatus Status { get; set; } = ShotStatus.Pending;

    /// <summary>Prompt mô tả hình của shot này. Lấy từ <see cref="PromptTemplate"/> + storyboard.</summary>
    public required string VisualPrompt { get; set; }

    /// <summary>Lời thoại của riêng shot này — dùng cho phụ đề và cho regenerate.</summary>
    public string? SpokenText { get; set; }

    // ── Timeline đã khoá (bước 5). KHÔNG đổi được sau khi render bắt đầu. ──────────────
    /// <remarks>
    /// Regenerate một shot KHÔNG được đổi <see cref="VideoDurationSeconds"/>: timeline đã khoá
    /// theo audio, đổi độ dài một shot là làm lệch mọi shot sau nó. Muốn đổi nhịp thì tạo job mới.
    /// </remarks>
    public double NarrationStartSeconds { get; set; }
    public double NarrationEndSeconds { get; set; }
    public int VideoDurationSeconds { get; set; }
    public double VideoStartSeconds { get; set; }
    public double AudioStartInTimelineSeconds { get; set; }
    public PaddingStrategyValue Padding { get; set; } = PaddingStrategyValue.None;

    /// <summary>Seed đã dùng. Retry đổi seed là cách retry có nghĩa duy nhất khi provider tôn trọng seed.</summary>
    public int? Seed { get; set; }

    public string? ProviderModelId { get; set; }

    /// <summary>Request id phía provider — để đối soát hoá đơn và khiếu nại.</summary>
    public string? ProviderRequestId { get; set; }

    /// <summary>Request id của lần gọi TTS. Shot kế tiếp dùng nó để nối ngữ điệu.</summary>
    public string? TtsRequestId { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Chi phí thật của riêng shot này, kể cả các lần retry.</summary>
    public decimal CostUsd { get; set; }

    public Guid? ClipAssetId { get; set; }

    /// <summary>Track tiếng gốc của provider, giữ lại để QC bước 9 chạy VAD.</summary>
    public Guid? NativeAudioAssetId { get; set; }

    /// <summary>Đã khớp môi chưa (bước 7, Sprint 5).</summary>
    public bool IsLipSynced { get; set; }

    public DateTime? RenderStartedAt { get; set; }
    public DateTime? RenderFinishedAt { get; set; }

    public AdVideoJob Job { get; set; } = null!;
    public MediaAsset? ClipAsset { get; set; }
    public MediaAsset? NativeAudioAsset { get; set; }
}

/// <summary>
/// Bản sao của <see cref="Core.Timeline.PaddingStrategy"/> để entity không phải tham chiếu namespace Timeline.
/// </summary>
/// <remarks>
/// Có vẻ trùng lặp, nhưng để <c>Shot</c> dùng thẳng enum bên Timeline thì một thay đổi ở tầng
/// tính toán sẽ âm thầm đổi ý nghĩa dữ liệu đã lưu trong DB. Hai enum tách rời + một hàm ánh xạ
/// ở Infrastructure là ranh giới rõ ràng hơn. Giá trị số phải giữ khớp nhau.
/// </remarks>
public enum PaddingStrategyValue
{
    None = 0,
    HoldLastFrame = 1,
    KenBurns = 2,
}
