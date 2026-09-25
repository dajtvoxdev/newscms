using AdVideo.Core.Enums;

namespace AdVideo.Core.Providers;

/// <summary>
/// Tra provider theo tên và theo yêu cầu nghiệp vụ. Cài đặt thật nằm ở Infrastructure và đọc
/// capability từ DB (D10).
/// </summary>
public interface IProviderRegistry
{
    /// <summary>Mọi provider video đang bật, kèm capability đã nạp từ DB.</summary>
    Task<IReadOnlyList<IVideoProvider>> GetVideoProvidersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ITtsProvider>> GetTtsProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lấy đúng một provider theo tên. Null nếu không có hoặc đang tắt.</summary>
    Task<IVideoProvider?> FindVideoProviderAsync(string providerName, CancellationToken cancellationToken = default);

    Task<ITtsProvider?> FindTtsProviderAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chọn provider theo YÊU CẦU NGHIỆP VỤ, không theo tên provider.
    /// </summary>
    /// <remarks>
    /// <b>Đây là chỗ thực thi Luật 3:</b> khách chọn tier và trả lời "có người xuất hiện không";
    /// hệ thống tự chọn provider. Tên provider chỉ hiện trong log và hoá đơn, không bao giờ
    /// hiện trong request từ khách.
    /// </remarks>
    Task<ProviderSelectionResult> SelectVideoProviderAsync(
        VideoRequirements requirements, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chọn engine TTS theo tier. Draft có thể dùng VieNeu (rẻ, tự host); thành phẩm phải có mốc thời gian.
    /// </summary>
    Task<ProviderSelectionResult> SelectTtsProviderAsync(
        VideoTier tier, CancellationToken cancellationToken = default);
}

/// <summary>Những gì khách thật sự quyết định. Không có trường nào tên là "provider".</summary>
public sealed record VideoRequirements
{
    public required VideoTier Tier { get; init; }

    /// <summary>
    /// Video có người xuất hiện không.
    /// </summary>
    /// <remarks>
    /// Đây là câu hỏi DUY NHẤT ngoài tier mà khách phải trả lời. Hỏi "bạn muốn Veo hay Kling"
    /// là bắt người làm marketing học thứ không liên quan đến việc của họ; hỏi "video có người
    /// không" thì ai cũng trả lời được.
    /// </remarks>
    public required bool HasPerson { get; init; }

    public required int TargetDurationSeconds { get; init; }

    public required AspectRatio AspectRatio { get; init; }

    /// <summary>Số chủ thể phải giữ được trong một khung. &gt;2 thì ưu tiên provider có <c>MaxSubjectsReliably</c> cao hơn tier.</summary>
    public int SubjectCount { get; init; } = 1;

    /// <summary>Cần nối frame không — chỉ định dạng daily (Sprint 3) mới bật.</summary>
    public bool NeedsFrameChaining { get; init; }
}

/// <summary>Kết quả chọn provider. Hoặc chọn được, hoặc biết CHÍNH XÁC vì sao không.</summary>
public sealed record ProviderSelectionResult
{
    public bool IsSuccess { get; init; }

    public string? ProviderName { get; init; }

    /// <summary>Lý do đọc được, trả thẳng cho người dùng cuối với HTTP 422.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// Provider nào LÀM ĐƯỢC việc này. Luôn điền kể cả khi thất bại.
    /// </summary>
    /// <remarks>
    /// 422 mà không kèm gợi ý là 422 vô dụng: khách biết mình sai nhưng không biết sửa thế nào.
    /// "Video có người không dùng được với tier Draft; tier Standard (Kling) thì được" là câu
    /// khách hành động được.
    /// </remarks>
    public IReadOnlyList<ProviderSuggestion> Suggestions { get; init; } = [];

    public static ProviderSelectionResult Success(string providerName) =>
        new() { IsSuccess = true, ProviderName = providerName };

    public static ProviderSelectionResult Failure(
        IReadOnlyList<string> reasons, IReadOnlyList<ProviderSuggestion>? suggestions = null) =>
        new() { IsSuccess = false, Reasons = reasons, Suggestions = suggestions ?? [] };
}

/// <summary>Một lựa chọn thay thế khả thi, kèm lý do nó khả thi và cái giá phải trả.</summary>
public sealed record ProviderSuggestion(
    string ProviderName,
    VideoTier Tier,
    string Reason,
    decimal? EstimatedCostUsd = null);
