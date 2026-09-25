namespace AdVideo.Core.Providers;

/// <summary>Manifest mô tả một engine TTS làm được gì. Nạp lúc chạy từ DB (D10).</summary>
public sealed record TtsProviderCapability
{
    public required string Provider { get; init; }

    public required string ModelId { get; init; }

    /// <summary>
    /// Có trả về mốc thời gian theo TỪ không.
    /// </summary>
    /// <remarks>
    /// <b>Đây là cánh cửa quyết định tier.</b> Không có mốc thời gian thì bước 5 (khoá timeline)
    /// mù hoàn toàn: không biết câu thoại dài bao nhiêu nên không thể chọn bậc thời lượng shot.
    /// Provider có <c>HasWordTimings = false</c> chỉ dùng được cho tier Nháp, nơi lệch tiếng-hình
    /// chấp nhận được.
    /// </remarks>
    public required bool HasWordTimings { get; init; }

    /// <summary>Có mốc tới từng ký tự không — tốt hơn theo từ, dùng cho phụ đề burn-in khớp từng chữ.</summary>
    public bool HasCharacterTimings { get; init; }

    /// <summary>Mã ngôn ngữ hỗ trợ. Tiếng Việt là <c>"vi"</c>.</summary>
    public required IReadOnlyList<string> SupportedLanguages { get; init; }

    /// <summary>Có clone giọng từ mẫu ngắn không. Kèm rủi ro pháp lý nặng — xem R6 và <see cref="Entities.ConsentRecord"/>.</summary>
    public bool SupportsVoiceCloning { get; init; }

    /// <summary>Độ dài mẫu tối thiểu để clone, tính bằng giây.</summary>
    public int MinCloneSampleSeconds { get; init; }

    /// <summary>
    /// Có bước xác minh chính chủ khi clone không.
    /// </summary>
    /// <remarks>
    /// ElevenLabs Professional Voice Clone có chạy voice-captcha; VieNeu tự host thì KHÔNG có
    /// lớp kiểm duyệt nào. Giá trị này không đổi hành vi kỹ thuật nhưng quyết định mức cảnh báo
    /// phải hiện cho người dùng và mức bằng chứng <see cref="Entities.ConsentRecord"/> phải thu.
    /// </remarks>
    public bool HasVoiceOwnershipVerification { get; init; }

    /// <summary>Có nhận <c>previous_text</c> / <c>previous_request_ids</c> để nối ngữ điệu giữa các shot không.</summary>
    public bool SupportsProsodyContinuation { get; init; }

    /// <summary>Giá mỗi 1000 ký tự, đô la. ElevenLabs tính theo ký tự chứ không theo giây audio.</summary>
    public decimal CostPer1000CharsUsd { get; init; }

    /// <summary>
    /// Real-time factor: sinh 1 giây audio mất bao nhiêu giây. Chỉ meaningful với engine tự host.
    /// </summary>
    /// <remarks>
    /// VieNeu công bố RTF 0.37 (int8) trên desktop CPU — phải đo lại trên VPS thật. RTF &gt; 1
    /// nghĩa là chậm hơn thời gian thực và không dùng được cho job có ngân sách thời gian.
    /// </remarks>
    public decimal? RealTimeFactor { get; init; }

    /// <summary>Giọng Default của ElevenLabs ngừng hoạt động 31/12/2026 — cờ này để chặn trước khi quá hạn.</summary>
    public DateTime? VoicesExpireAt { get; init; }
}
