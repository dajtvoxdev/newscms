using AdVideo.Core.Enums;

namespace AdVideo.Core.Providers;

/// <summary>
/// Lý do một lần gọi video thất bại. Phân loại này quyết định retry hay fail — không phải
/// mã HTTP quyết định.
/// </summary>
public enum VideoFailureKind
{
    /// <summary>Thành công. Không phải lỗi.</summary>
    None = 0,

    /// <summary>
    /// Provider từ chối NỘI DUNG (ảnh có mặt người, prompt vi phạm chính sách).
    /// </summary>
    /// <remarks>
    /// <b>Retry là vô nghĩa và tốn tiền</b> — cùng nội dung đó sẽ bị từ chối lại. Phải báo lên
    /// người dùng với lý do đọc được để họ đổi brief hoặc đổi tier. Đây cũng là chỗ ghi lại
    /// nguyên văn lỗi cho Sprint 0 (câu hỏi nghiệm thu số 4).
    /// </remarks>
    ContentRejected = 1,

    /// <summary>Lỗi mạng/timeout tạm thời. Retry được ngay.</summary>
    Transient = 2,

    /// <summary>Bị giới hạn tần suất. Retry được nhưng PHẢI chờ — theo <see cref="VideoResult.RetryAfterSeconds"/> nếu có.</summary>
    RateLimited = 3,

    /// <summary>
    /// Provider chết hoặc đổi API. <b>Fail cả job</b> theo Luật 1, không tự đổi provider.
    /// </summary>
    /// <remarks>
    /// Hai model khác nhau cho ra hai phong cách hình khác nhau; ghép vào một video thì lộ rõ
    /// ở chỗ chuyển cảnh. Thà mời khách render lại còn hơn giao một video dán hai kiểu hình.
    /// </remarks>
    ProviderUnavailable = 4,

    /// <summary>Không phân loại được. Xử lý như Transient nhưng log nguyên văn để bổ sung phân loại sau.</summary>
    Unknown = 5,
}

/// <summary>Yêu cầu sinh một shot video.</summary>
public sealed record VideoRequest
{
    public required Guid JobId { get; init; }

    /// <summary>Thứ tự shot trong job, 0-based. Dùng đặt tên object trong storage.</summary>
    public required int ShotIndex { get; init; }

    public required string Prompt { get; init; }

    /// <summary>Thời lượng giây — PHẢI là một bậc trong <see cref="VideoProviderCapability.AllowedDurationSeconds"/>.</summary>
    public required int DurationSeconds { get; init; }

    public required AspectRatio AspectRatio { get; init; }

    /// <summary>
    /// Ảnh tham chiếu, theo thứ tự ưu tiên giảm dần. Phần tử đầu là ảnh đầu vào (image-to-video).
    /// </summary>
    /// <remarks>
    /// Sprint 3 sẽ gửi LẶP LẠI ảnh gốc ở mọi đoạn để chống trôi hình — chỉ dựa vào khung cuối
    /// của đoạn trước là để sai số cộng dồn. Số lượng bị giới hạn bởi
    /// <see cref="VideoProviderCapability.MaxReferenceImages"/>.
    /// </remarks>
    public IReadOnlyList<string> ReferenceImageUrls { get; init; } = [];

    /// <summary>
    /// Prompt phủ định. Lấy từ <see cref="Entities.PromptTemplate"/> trong DB, không hard-code trong adapter.
    /// </summary>
    /// <remarks>
    /// Adapter nào có trường <c>negative_prompt</c> riêng thì gửi cả hai nơi: trong prompt dương
    /// VÀ trong trường này. Không phải provider nào cũng có trường riêng, nên câu phủ định
    /// nằm sẵn trong prompt dương là lớp phòng thủ chung.
    /// </remarks>
    public string? NegativePrompt { get; init; }

    /// <summary>Seed cố định. Đổi seed là cách retry có nghĩa duy nhất khi provider tôn trọng seed.</summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Yêu cầu provider KHÔNG sinh tiếng. Kết quả thật có được tôn trọng không thì xem
    /// <see cref="VideoResult.HasNativeAudio"/> — đừng tin tham số đã gửi.
    /// </summary>
    public bool SuppressNativeAudio { get; init; } = true;

    /// <summary>URL để provider gọi lại khi xong. Null = adapter tự poll.</summary>
    public string? WebhookCallbackUrl { get; init; }
}

/// <summary>Kết quả một lần gọi video provider.</summary>
public sealed record VideoResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Id phía provider — bắt buộc lưu để đối soát hoá đơn và để khiếu nại.</summary>
    public string? ProviderRequestId { get; init; }

    /// <summary>
    /// URL video. <b>CÓ THỜI HẠN — phải tải về object storage ngay.</b>
    /// </summary>
    /// <remarks>
    /// Veo giữ file trên server Google khoảng 2 ngày rồi xoá. Lưu URL vào DB thay vì tải file
    /// về là một quả mìn hẹn giờ: job "thành công", khách tải được hôm nay, và link chết
    /// vào thứ Năm tuần sau.
    /// </remarks>
    public Uri? VideoUri { get; init; }

    /// <summary>Bytes video, khi provider trả thẳng thay vì trả URL.</summary>
    public byte[]? VideoBytes { get; init; }

    /// <summary>
    /// File THẬT CÓ track audio không. Đo bằng ffprobe sau khi tải về, không phải đọc từ response.
    /// </summary>
    public bool HasNativeAudio { get; init; }

    /// <summary>Độ dài giây đo từ file thật. Provider báo một đằng, file một nẻo là chuyện có thật.</summary>
    public double MeasuredDurationSeconds { get; init; }

    /// <summary>Chi phí provider BÁO VỀ. Có thể null (fal.ai không trả cost trong response).</summary>
    public decimal? ReportedCostUsd { get; init; }

    public VideoFailureKind FailureKind { get; init; } = VideoFailureKind.None;

    /// <summary>Lý do đọc được, dành cho người dùng cuối. Không phải chuỗi lỗi kỹ thuật.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Lỗi NGUYÊN VĂN từ provider. Chỉ dùng để log và để điền kết luận Sprint 0 — không bao giờ trả ra API.
    /// </summary>
    public string? RawError { get; init; }

    /// <summary>Giây nên chờ trước khi retry, lấy từ header <c>Retry-After</c> nếu có.</summary>
    public int? RetryAfterSeconds { get; init; }

    public bool CanRetry => FailureKind is VideoFailureKind.Transient or VideoFailureKind.RateLimited or VideoFailureKind.Unknown;
}

/// <summary>
/// Trừu tượng hoá một provider sinh video. Mọi provider phải cài interface này — kể cả
/// <c>FakeVideoProvider</c> dùng cho test.
/// </summary>
/// <remarks>
/// <b>Adapter phải CHUẨN HOÁ, không chuyển tiếp</b> (Luật 2). Không ném nguyên JSON lỗi của
/// nhà cung cấp ra ngoài: người gọi cần biết "provider này chỉ hỗ trợ 4, 6 hoặc 8 giây",
/// không cần đọc stack trace của fal.ai.
/// </remarks>
public interface IVideoProvider
{
    /// <summary>Danh tính provider, khớp <see cref="ProviderNames"/> và cột <c>Provider</c> trong DB.</summary>
    string Name { get; }

    /// <summary>Manifest nạp lúc chạy. KHÔNG hard-code trong adapter — provider đổi giới hạn thì sửa DB.</summary>
    VideoProviderCapability Capability { get; }

    /// <summary>
    /// Sinh một shot. Phương thức này KHÔNG được ném exception cho lỗi nghiệp vụ — mọi thất bại
    /// phải về qua <see cref="VideoResult.FailureKind"/> để caller quyết định retry hay fail.
    /// </summary>
    Task<VideoResult> GenerateAsync(VideoRequest request, CancellationToken cancellationToken = default);

    /// <summary>Provider còn sống không. Smoke test hằng ngày gọi cái này (Sprint 5).</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
