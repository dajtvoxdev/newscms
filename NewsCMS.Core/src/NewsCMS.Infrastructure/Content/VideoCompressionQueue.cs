using System.Threading.Channels;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Hàng đợi trong tiến trình cho công việc nén video — Channel không giới hạn, an toàn dùng
/// đồng thời (Writer từ request thread, Reader từ <see cref="VideoCompressionWorker"/>).
///
/// Cố ý KHÔNG bền vững qua restart (khác <c>MediaUploadSession</c> theo dõi tus): nén video là
/// tối ưu dung lượng, không phải dữ liệu — mất một job đang chờ khi app restart giữa chừng chỉ
/// có nghĩa video đó giữ nguyên kích thước gốc, KHÔNG mất nội dung hay gãy URL. Đánh đổi hợp lý
/// để tránh thêm bảng DB + logic sweep-lại chỉ cho một tính năng "nice to have".
/// </summary>
public interface IVideoCompressionQueue
{
    /// <summary>Xếp một Media (đã lưu xong, kind=video) vào hàng đợi nén. Không chặn, không throw.</summary>
    void Enqueue(Guid mediaId);

    /// <summary>Reader để <see cref="VideoCompressionWorker"/> tiêu thụ tuần tự.</summary>
    ChannelReader<Guid> Reader { get; }
}

public sealed class VideoCompressionQueue : IVideoCompressionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,   // chỉ VideoCompressionWorker đọc — nén tuần tự, không chồng CPU
        SingleWriter = false   // nhiều request upload đồng thời có thể ghi
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid mediaId)
    {
        // TryWrite trên unbounded channel luôn thành công trừ khi đã Complete() — không xảy ra
        // ở đây (không ai gọi Complete), nhưng vẫn kiểm tra để không throw nếu lỡ có sau này.
        _channel.Writer.TryWrite(mediaId);
    }
}
