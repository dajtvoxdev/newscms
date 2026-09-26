using AdVideo.Core.Providers;

namespace AdVideo.Infrastructure.Providers.Fake;

/// <summary>
/// Điều khiển hành vi của provider giả.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao provider giả phải cấu hình được cách HỎNG:</b> đường thành công thì test nào cũng
/// đi qua. Thứ cần chứng minh ở Sprint 1 là pipeline xử lý đúng khi một shot bị từ chối nội dung,
/// khi provider trả 429, khi lần thử thứ nhất fail và lần thứ hai được. Không có cần gạt thì
/// những nhánh đó chỉ chạy lần đầu tiên gặp sự cố thật — lúc đang có khách chờ.
/// </para>
/// <para>
/// Mọi giá trị mặc định đều là "chạy trơn tru": bật lỗi phải là hành động cố ý của test.
/// </para>
/// </remarks>
public sealed class FakeProviderOptions
{
    public const string SectionName = "AdVideo:FakeProviders";

    /// <summary>
    /// Có đăng ký provider giả vào DI không.
    /// </summary>
    /// <remarks>
    /// <b>Mặc định TẮT trong code</b> — muốn dùng phải bật tường minh trong
    /// <c>appsettings.Development.json</c>, biến môi trường, hoặc test. Trước đây mặc định bật,
    /// nên một host production thiếu section này là có provider giả trong danh sách: credential
    /// thật hỏng thì provider giả âm thầm nhận việc và khách nhận về video testsrc2.
    /// Bật trên môi trường Production thì host <b>từ chối khởi động</b>
    /// (<c>DependencyInjection.AddProviders</c>).
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Giả lập độ trễ mỗi lần gọi, mili giây. 0 = trả về ngay.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Shot có chỉ số nằm trong danh sách này sẽ fail. Rỗng = không shot nào fail.</summary>
    public IList<int> FailShotIndexes { get; } = [];

    /// <summary>Kiểu lỗi trả về cho shot bị ép fail.</summary>
    public VideoFailureKind FailureKind { get; set; } = VideoFailureKind.Transient;

    /// <summary>
    /// Số lần fail rồi mới thành công, áp cho shot trong <see cref="FailShotIndexes"/>.
    /// </summary>
    /// <remarks>
    /// Để kiểm tra retry thật sự có tác dụng, chứ không chỉ kiểm tra rằng nó có chạy: đặt 1 nghĩa
    /// là lần đầu hỏng, lần hai được — đúng hình dạng của một lỗi tạm thời.
    /// </remarks>
    public int FailTimesBeforeSuccess { get; set; }

    /// <summary>Cho TTS trả về kết quả không có mốc thời gian, để kiểm tra nhánh không khoá được timeline.</summary>
    public bool TtsWithoutTimings { get; set; }

    /// <summary>Ping luôn trả về giá trị này. Đặt false để kiểm tra nhánh provider chết.</summary>
    public bool PingSucceeds { get; set; } = true;
}
