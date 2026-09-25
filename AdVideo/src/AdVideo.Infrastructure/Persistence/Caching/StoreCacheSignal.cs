using Microsoft.Extensions.Primitives;

namespace AdVideo.Infrastructure.Persistence.Caching;

/// <summary>
/// Cần gạt để xoá toàn bộ cache của một store, dùng chung cho mọi scope trong tiến trình.
/// </summary>
/// <typeparam name="TOwner">Store sở hữu tín hiệu này — chỉ để tách các cache ra khỏi nhau.</typeparam>
/// <remarks>
/// <para>
/// <b>Vì sao không xoá từng key:</b> store là scoped còn <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// là singleton. Gọi <c>Invalidate()</c> trong một request mà chỉ xoá key mình biết thì request
/// khác vẫn đọc key cũ. Token huỷ thì mọi entry đã đăng ký nó rụng cùng lúc, không sót.
/// </para>
/// <para>
/// <b>Vì sao tách theo <typeparamref name="TOwner"/>:</b> nạp lại key provider không có lý do gì
/// làm rụng cache prompt. Một tín hiệu dùng chung sẽ biến mỗi lần đổi credential thành một đợt
/// dội DB của cả ba store.
/// </para>
/// </remarks>
public sealed class StoreCacheSignal<TOwner> : IDisposable
{
    private CancellationTokenSource _source = new();

    /// <summary>Token gắn vào từng cache entry.</summary>
    public IChangeToken Token => new CancellationChangeToken(_source.Token);

    /// <summary>Làm rụng mọi entry đã gắn token.</summary>
    public void Reset()
    {
        // Đổi nguồn TRƯỚC khi huỷ: nếu huỷ trước thì entry nào được tạo giữa hai lệnh sẽ gắn vào
        // token đã huỷ và rụng ngay khi vừa ghi, khiến cache không bao giờ giữ được gì.
        var previous = Interlocked.Exchange(ref _source, new CancellationTokenSource());

        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose() => _source.Dispose();
}
