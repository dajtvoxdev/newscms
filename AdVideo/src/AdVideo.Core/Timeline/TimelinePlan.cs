using AdVideo.Core.Providers;

namespace AdVideo.Core.Timeline;

/// <summary>Cách đặt track thoại lên các shot.</summary>
public enum AudioLayoutStrategy
{
    /// <summary>
    /// Thoại của mỗi shot bắt đầu ĐÚNG lúc shot đó bắt đầu; phần video thừa thành im lặng trong shot.
    /// </summary>
    /// <remarks>
    /// Lip-sync offset = 0 ở mọi shot, theo cấu tạo. Giá phải trả: thoại có khoảng lặng ở chỗ cắt.
    /// Khoảng lặng đó vô hại NẾU chỗ cắt nằm ở một chỗ ngừng tự nhiên — đó là lý do
    /// <see cref="TimelineLocker"/> chỉ cắt ở im lặng.
    /// </remarks>
    PadPerShot = 0,

    /// <summary>
    /// Thoại chạy liên tục không chèn lặng; video cắt theo lưới thời lượng.
    /// </summary>
    /// <remarks>
    /// Lip-sync offset TÍCH LUỸ theo tổng padding và phải &lt; 200 ms. Chỉ dùng khi phần dư rất nhỏ,
    /// vì sai lệch cộng dồn qua 4–6 shot là thứ người xem cảm thấy mà không gọi tên được.
    /// </remarks>
    ContinuousAudio = 1,
}

/// <summary>Phần video thừa ra (so với thoại) được bù bằng gì.</summary>
public enum PaddingStrategy
{
    /// <summary>Không có phần thừa — audio khớp đúng một bậc lưới.</summary>
    None = 0,

    /// <summary>Giữ khung cuối đứng yên. Rẻ, nhưng dễ trông như video bị đơ.</summary>
    HoldLastFrame = 1,

    /// <summary>Zoom/pan nhẹ trên khung cuối. Tốn thêm một lần encode nhưng không trông như bị đơ.</summary>
    KenBurns = 2,
}

/// <summary>Một đoạn thoại đã được gán thời lượng video.</summary>
public sealed record ShotPlan
{
    public required int Index { get; init; }

    /// <summary>Đoạn thoại này nằm ở đâu trong track thoại GỐC (do TTS trả về).</summary>
    public required double NarrationStartSeconds { get; init; }
    public required double NarrationEndSeconds { get; init; }

    /// <summary>Thời lượng video, LUÔN là một bậc trong lưới của provider.</summary>
    public required int VideoDurationSeconds { get; init; }

    /// <summary>Lời thoại của đoạn này — dùng cho prompt shot và cho phụ đề.</summary>
    public required string SpokenText { get; init; }

    /// <summary>Vị trí bắt đầu của shot trong video cuối.</summary>
    public required double VideoStartSeconds { get; init; }

    /// <summary>Vị trí bắt đầu của đoạn thoại này trong track âm thanh cuối.</summary>
    public required double AudioStartInTimelineSeconds { get; init; }

    public required PaddingStrategy Padding { get; init; }

    public double NarrationDurationSeconds => NarrationEndSeconds - NarrationStartSeconds;

    /// <summary>Phần video thừa so với thoại. Luôn ≥ 0 vì thời lượng được làm tròn LÊN.</summary>
    public double PaddingSeconds => VideoDurationSeconds - NarrationDurationSeconds;

    /// <summary>
    /// Lệch giữa lúc thoại thật sự vang lên và lúc nó ĐÁNG LẼ vang lên nếu audio không bị ngắt.
    /// </summary>
    /// <remarks>
    /// Với <see cref="AudioLayoutStrategy.PadPerShot"/> giá trị này là 0 ở mọi shot — thoại luôn
    /// bắt đầu cùng shot. Với <see cref="AudioLayoutStrategy.ContinuousAudio"/> nó tích luỹ và
    /// phải được giữ dưới ngưỡng.
    /// </remarks>
    public required double LipSyncOffsetSeconds { get; init; }
}

/// <summary>Kế hoạch timeline đã khoá. Bất biến — bước 6 đọc nó, không sửa nó.</summary>
public sealed record TimelinePlan
{
    /// <summary>Có dùng được không. <c>false</c> thì job phải fail kèm <see cref="Reasons"/>, không được âm thầm ghép video sai.</summary>
    public required bool IsAcceptable { get; init; }

    public required IReadOnlyList<ShotPlan> Shots { get; init; }

    /// <summary>Lý do không chấp nhận được, viết cho người vận hành đọc.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    public required double TotalVideoSeconds { get; init; }

    public required double TotalNarrationSeconds { get; init; }

    /// <summary>Lệch lip-sync lớn nhất across các shot.</summary>
    public required double MaxLipSyncOffsetSeconds { get; init; }

    /// <summary>
    /// Cảnh báo không chặn: ví dụ phải cắt giữa câu vì không tìm thấy chỗ im lặng.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Có shot nào phải cắt ngoài chỗ im lặng không. Nếu có thì nghe sẽ hụt.</summary>
    public bool HasMidSentenceCut { get; init; }

    public static TimelinePlan Reject(IReadOnlyList<string> reasons, IReadOnlyList<string>? warnings = null) =>
        new()
        {
            IsAcceptable = false,
            Shots = [],
            Reasons = reasons,
            TotalVideoSeconds = 0,
            TotalNarrationSeconds = 0,
            MaxLipSyncOffsetSeconds = 0,
            Warnings = warnings ?? [],
        };
}

/// <summary>Đầu vào cho việc khoá timeline.</summary>
public sealed record TimelineLockRequest
{
    /// <summary>Độ dài track thoại thật, đo từ audio TTS trả về. Không phải ước tính từ số ký tự.</summary>
    public required double NarrationDurationSeconds { get; init; }

    /// <summary>Mốc thời gian theo từ. Rỗng thì không cắt ở im lặng được — sẽ phải cắt giữa câu.</summary>
    public IReadOnlyList<WordTiming> WordTimings { get; init; } = [];

    /// <summary>Lưới thời lượng của provider đã chọn, ví dụ <c>[4, 6, 8]</c> cho Veo.</summary>
    public required IReadOnlyList<int> DurationGrid { get; init; }

    /// <summary>Chiến lược đặt thoại. Mặc định an toàn nhất: lip-sync offset bằng 0.</summary>
    public AudioLayoutStrategy Layout { get; init; } = AudioLayoutStrategy.PadPerShot;

    /// <summary>Ngưỡng lệch lip-sync tối đa. 0.2 giây = 200 ms theo tiêu chí thành công S4.</summary>
    public double MaxLipSyncOffsetSeconds { get; init; } = 0.2;

    /// <summary>Số shot tối đa. Nhiều hơn thì video dài vô lý và chi phí tăng theo cấp số.</summary>
    public int MaxShots { get; init; } = 8;

    /// <summary>Khoảng lặng ngắn nhất được coi là chỗ cắt hợp lý.</summary>
    public double SilenceThresholdSeconds { get; init; } = 0.25;

    /// <summary>
    /// Đoạn thoại đã được đạo diễn chia sẵn (Sprint 2 trở đi). Để null thì tự chia theo lưới.
    /// </summary>
    /// <remarks>
    /// Sprint 1 không có LLM đạo diễn nên để null. Sprint 2 điền vào đây — và đó là lý do
    /// hàm chia sẵn được tách thành <c>LockSegments</c> thay vì nhét thêm cờ vào hàm này.
    /// </remarks>
    public IReadOnlyList<NarrationSegment>? PresetSegments { get; init; }
}

/// <summary>Một đoạn thoại do lớp đạo diễn chia sẵn.</summary>
public sealed record NarrationSegment(string Text, double StartSeconds, double EndSeconds);
