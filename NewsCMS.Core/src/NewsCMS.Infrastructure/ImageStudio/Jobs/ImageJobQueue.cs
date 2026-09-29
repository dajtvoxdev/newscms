using System.Threading.Channels;

namespace NewsCMS.Infrastructure.ImageStudio.Jobs;

/// <summary>
/// Hàng đợi trong tiến trình, chỉ chứa id. Khác <c>VideoCompressionQueue</c>, job nằm trong DB
/// trước khi vào đây: restart làm rơi hàng đợi thì <see cref="ImageJobWorker"/> nạp lại từ DB.
/// </summary>
public interface IImageJobQueue
{
    void Enqueue(Guid jobId);

    ChannelReader<Guid> Reader { get; }
}

public sealed class ImageJobQueue : IImageJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false, // nhiều consumer theo ImageStudio:MaxConcurrency
        SingleWriter = false,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid jobId) => _channel.Writer.TryWrite(jobId);
}
