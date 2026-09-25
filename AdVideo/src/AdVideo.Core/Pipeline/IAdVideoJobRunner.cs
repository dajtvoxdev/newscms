namespace AdVideo.Core.Pipeline;

/// <summary>
/// Hợp đồng giữa API (đẩy việc vào hàng đợi) và Worker (chạy việc).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao interface này nằm ở Core.</b> Hangfire lưu vào DB <b>tên kiểu</b> của phương thức
/// được enqueue, rồi worker đọc tên đó ra và nhờ DI dựng lại. Nghĩa là hai tiến trình phải cùng
/// biết một kiểu. Nếu API enqueue thẳng lớp cài đặt trong <c>AdVideo.Worker</c> thì API phải
/// tham chiếu Worker — hai host phụ thuộc chéo nhau, và mỗi lần sửa một bước pipeline là một lần
/// phải deploy lại API. Một interface ở Core cắt đúng chỗ đó.
/// </para>
/// <para>
/// <b>Đừng đổi tên kiểu hay tên phương thức này khi đang có job trong hàng đợi.</b> Job đã nằm
/// trong bảng Hangfire mang tên cũ; đổi tên là biến chúng thành job không bao giờ chạy được, im
/// lặng, cho tới khi ai đó để ý hàng đợi dài ra.
/// </para>
/// </remarks>
public interface IAdVideoJobRunner
{
    /// <summary>
    /// Chạy toàn bộ pipeline cho một job.
    /// </summary>
    /// <remarks>
    /// Chỉ nhận <paramref name="jobId"/> chứ không nhận cả object job: tham số của Hangfire được
    /// serialize vào DB và có thể nằm đó hàng giờ. Truyền nguyên trạng thái job nghĩa là worker
    /// làm việc trên một bản chụp đã cũ — trong khi job có thể đã bị huỷ.
    /// </remarks>
    Task RunAsync(Guid jobId, CancellationToken cancellationToken = default);
}
