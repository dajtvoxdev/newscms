namespace NewsCMS.Application.Builder;

/// <summary>
/// Khối động biết đếm số bản ghi KHỚP bộ lọc, không tính giới hạn hiển thị.
///
/// Vì sao cần: lọc ra 0 kết quả mà panel không nói gì thì người dùng không biết vì sao khối trống —
/// do chuyên mục sai, do "chỉ bài nổi bật", hay do site chưa có bài nào. Có số này thì panel hiện
/// được "Khớp 12 bài, hiển thị 6" và trường hợp 0 trở nên tự giải thích.
///
/// Tuỳ chọn: khối không lấy dữ liệu theo danh sách (breadcrumb, pdf-flipbook) không cần cài.
/// </summary>
public interface IBlockMatchCounter
{
    /// <summary>
    /// Số bản ghi khớp bộ lọc trong props, BỎ QUA count/skip. Không đếm được thì trả null
    /// (đừng throw — panel chỉ mất một dòng thông tin, không được làm hỏng preview).
    /// </summary>
    Task<int?> CountMatchesAsync(DynamicBlockContext context, CancellationToken ct = default);
}
