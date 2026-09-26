namespace AdVideo.Core.Providers;

/// <summary>Mốc thời gian của một từ. Là đầu vào của bước 5 (khoá timeline) và của phụ đề burn-in.</summary>
/// <param name="Word">Từ nguyên văn như đã gửi.</param>
/// <param name="StartSeconds">Giây bắt đầu, tính từ đầu file audio.</param>
/// <param name="EndSeconds">Giây kết thúc.</param>
public sealed record WordTiming(string Word, double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}

/// <summary>Mốc thời gian của một ký tự — mịn hơn theo từ, dùng cho phụ đề khớp từng chữ.</summary>
public sealed record CharacterTiming(char Character, double StartSeconds, double EndSeconds);

/// <summary>Yêu cầu sinh giọng đọc.</summary>
public sealed record TtsRequest
{
    public required Guid JobId { get; init; }

    /// <summary>
    /// Văn bản ĐÃ áp từ điển phát âm. Adapter không tự sửa văn bản.
    /// </summary>
    /// <remarks>
    /// Tách việc áp <see cref="Entities.PronunciationDictionary"/> ra khỏi adapter là có chủ đích:
    /// nếu mỗi adapter tự sửa văn bản thì hai provider sẽ đọc khác nhau và không so sánh được.
    /// </remarks>
    public required string Text { get; init; }

    public required string VoiceId { get; init; }

    /// <summary>Tốc độ đọc. 1.0 là bình thường.</summary>
    public decimal Speed { get; init; } = 1.0m;

    /// <summary>
    /// Đoạn văn bản của lần gọi trước, để provider nối ngữ điệu.
    /// </summary>
    /// <remarks>
    /// Không có trường này thì mỗi shot nghe như một người đọc lại từ đầu — lộ rõ nhất ở chỗ
    /// chuyển cảnh. ElevenLabs hỗ trợ qua <c>previous_text</c>.
    /// </remarks>
    public string? PreviousText { get; init; }

    /// <summary>Request id của lần gọi trước — cách nối ngữ điệu chính xác hơn <see cref="PreviousText"/>.</summary>
    public string? PreviousRequestId { get; init; }

    /// <summary>
    /// Yêu cầu trả mốc thời gian. Bật mặc định vì không có mốc thì không khoá được timeline.
    /// </summary>
    public bool WithTimestamps { get; init; } = true;
}

/// <summary>Kết quả sinh giọng đọc.</summary>
public sealed record TtsResult
{
    public required bool IsSuccess { get; init; }

    public byte[]? AudioBytes { get; init; }

    /// <summary>Loại nội dung, ví dụ <c>"audio/mpeg"</c>. Cần để đặt đúng đuôi file trong storage.</summary>
    public string AudioContentType { get; init; } = "audio/mpeg";

    /// <summary>
    /// Độ dài audio THẬT, đo từ bytes trả về. Đây là con số bước 5 dùng để khoá timeline —
    /// không phải độ dài ước tính từ số ký tự.
    /// </summary>
    public double AudioDurationSeconds { get; init; }

    public IReadOnlyList<WordTiming> WordTimings { get; init; } = [];

    public IReadOnlyList<CharacterTiming> CharacterTimings { get; init; } = [];

    public string? ProviderRequestId { get; init; }

    /// <summary>Chi phí provider báo về. ElevenLabs tính theo ký tự — xem header <c>xi-character-count</c>.</summary>
    public decimal? ReportedCostUsd { get; init; }

    /// <summary>Chi phí adapter TỰ SUY. Vào sổ với <c>CostIsReported = false</c>. Null thì bước gọi suy từ capability.</summary>
    public decimal? EstimatedCostUsd { get; init; }

    /// <summary>SHA-256 của descriptor đã sinh ra kết quả này. Null = adapter viết tay.</summary>
    public string? DescriptorSha256 { get; init; }

    /// <summary>Số ký tự bị tính tiền, để đối soát hoá đơn.</summary>
    public int? BilledCharacterCount { get; init; }

    public VideoFailureKind FailureKind { get; init; } = VideoFailureKind.None;

    public string? FailureReason { get; init; }

    /// <summary>Lỗi nguyên văn — chỉ để log, không trả ra API.</summary>
    public string? RawError { get; init; }

    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// Kết quả này có đủ dữ liệu để khoá timeline không.
    /// </summary>
    /// <remarks>
    /// Trả false khi provider nói thành công nhưng không kèm mốc thời gian. Đó là trường hợp
    /// nguy hiểm nhất: nếu tin <see cref="IsSuccess"/> alone thì bước 5 sẽ chạy với dữ liệu rỗng
    /// và cho ra timeline sai mà không ai biết.
    /// </remarks>
    public bool CanLockTimeline => IsSuccess && (WordTimings.Count > 0 || CharacterTimings.Count > 0);
}

/// <summary>
/// Trừu tượng hoá một engine TTS. Hai adapter (ElevenLabs + VieNeu) ngay từ Sprint 1 KHÔNG phải
/// để dự phòng — mà để ép interface này đủ tổng quát. Một adapter duy nhất thì interface sẽ vô
/// tình bám sát hình dạng của nhà cung cấp đó, và cái thứ hai sẽ đòi refactor.
/// </summary>
public interface ITtsProvider
{
    string Name { get; }

    TtsProviderCapability Capability { get; }

    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken = default);

    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
