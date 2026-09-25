using AdVideo.Core.Enums;

namespace AdVideo.Core.Providers;

/// <summary>
/// Manifest mô tả một model video làm được gì. Nạp lúc chạy từ DB (D10), không hard-code.
/// </summary>
/// <remarks>
/// Tách riêng khỏi <see cref="TtsProviderCapability"/>: gộp chung sẽ cho phép những tổ hợp
/// vô nghĩa như "một provider video có HasWordTimings", và validator sẽ phải phòng thủ
/// thay vì chỉ đọc.
/// </remarks>
public sealed record VideoProviderCapability
{
    /// <summary>Tên provider, ví dụ <c>"kling"</c>. Khớp <see cref="ProviderNames"/>.</summary>
    public required string Provider { get; init; }

    /// <summary>Model id gửi lên API, ví dụ <c>"fal-ai/kling-video/v2.1/standard/image-to-video"</c>.</summary>
    /// <remarks>Provider đổi model id thường xuyên. Đây là lý do nó nằm trong DB chứ không trong code.</remarks>
    public required string ModelId { get; init; }

    /// <summary>
    /// Lưới thời lượng cho phép, tính bằng giây, ĐÃ sắp tăng dần.
    /// </summary>
    /// <remarks>
    /// Không phải provider nào cũng nhận thời lượng tuỳ ý: Kling chỉ có 5 và 10, Veo có 4/6/8.
    /// <see cref="Core.Timeline.TimelineLocker"/> làm tròn lên bậc gần nhất của lưới này —
    /// đó là lý do bước 5 phải chạy SAU bước 4 và TRƯỚC bước 6.
    /// </remarks>
    public required IReadOnlyList<int> AllowedDurationSeconds { get; init; }

    public required IReadOnlyList<AspectRatio> SupportedAspectRatios { get; init; }

    /// <summary>
    /// Có nhận ảnh đầu vào có mặt người không.
    /// </summary>
    /// <remarks>
    /// Veo được ghi nhận là TỰ CHẶN ảnh có mặt người. Đây không chỉ là giới hạn kỹ thuật mà
    /// còn là tín hiệu về mức rủi ro pháp lý (R5 — quyền hình ảnh cá nhân): nhà cung cấp lớn
    /// chọn cách chặn thẳng.
    /// </remarks>
    public required bool AcceptsHumanFaces { get; init; }

    /// <summary>Model có sinh ra track tiếng kèm video không.</summary>
    public required bool GeneratesNativeAudio { get; init; }

    /// <summary>
    /// Nếu có sinh tiếng thì mặc định là BẬT hay TẮT.
    /// </summary>
    /// <remarks>
    /// <b>Vidu mặc định BẬT.</b> Quên trường này thì video ra có hai lớp tiếng chồng voice-over —
    /// lỗi dễ quên nhất trong cả dự án và cũng khó lần ra nhất, vì người xem chỉ thấy "tiếng kỳ kỳ".
    /// </remarks>
    public required bool NativeAudioEnabledByDefault { get; init; }

    /// <summary>
    /// Provider có cho tách tiếng động (sfx) khỏi thoại của model không.
    /// </summary>
    /// <remarks>
    /// Quyết định này hiện thực hoá D3: "tắt THOẠI của model, không nhất thiết tắt TIẾNG ĐỘNG" —
    /// tiếng động đồng bộ theo chuyển động thì model làm rất tốt và nên giữ ở −18 dB.
    /// Nhưng phần lớn provider trả về MỘT track trộn sẵn; khi đó không thể giữ sfx mà bỏ thoại,
    /// nên buộc phải strip toàn bộ. Cờ này phân biệt hai trường hợp.
    /// </remarks>
    public bool CanSeparateSfxFromSpeech { get; init; }

    /// <summary>Có nhận ảnh làm khung đầu không. Sprint 1 chỉ dùng image-to-video.</summary>
    public required bool SupportsImageToVideo { get; init; }

    /// <summary>
    /// Có nhận khung cuối của video trước làm ảnh đầu vào không — điều kiện cần cho định dạng daily (Sprint 3).
    /// </summary>
    public bool SupportsFrameChaining { get; init; }

    /// <summary>Số ảnh tham chiếu tối đa một lần gọi. Chống trôi hình ở Sprint 3 cần gửi lại ảnh gốc mọi đoạn.</summary>
    public int MaxReferenceImages { get; init; }

    /// <summary>
    /// Số chủ thể giữ được đồng thời mà vẫn nhận ra được. Lý do duy nhất để cân nhắc Vidu.
    /// </summary>
    /// <remarks>Giá trị 0 nghĩa là "chưa đo" — Sprint 0 (T0.5) mới cho con số thật. Đừng đoán.</remarks>
    public int MaxSubjectsReliably { get; init; }

    /// <summary>Tier nào provider này phục vụ được. Luật 3: khách chọn tier, hệ thống suy ra provider.</summary>
    public required IReadOnlyList<VideoTier> ServesTiers { get; init; }

    /// <summary>Giá mỗi giây video, đô la. Dùng để ước tính chi phí TRƯỚC khi gọi và đối soát <see cref="Entities.ProviderCall"/> sau khi gọi.</summary>
    public decimal CostPerSecondUsd { get; init; }

    /// <summary>Seed có được tôn trọng không. Nếu không thì "retry cùng provider với seed khác" vô nghĩa.</summary>
    public bool SupportsSeed { get; init; }

    /// <summary>Có trả về lời thoại/lip-sync được không — cần cho bước 7 (Sprint 5).</summary>
    public bool SupportsLipSync { get; init; }

    /// <summary>Thời lượng dài nhất một lần gọi. Vượt quá thì phải chia shot.</summary>
    public int MaxDurationSeconds => AllowedDurationSeconds.Count == 0
        ? 0
        : AllowedDurationSeconds[^1];
}
