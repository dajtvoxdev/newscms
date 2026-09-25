using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

/// <summary>
/// Một khách hàng dùng dịch vụ. Trong bối cảnh hiện tại: mỗi site NewsCMS là một tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bảng này là gốc của mọi global query filter.</b> Mọi entity mang <c>TenantId</c> đều trỏ về
/// đây, và <c>TenantId</c> của request đến từ đúng một chỗ: key trong header
/// <c>X-AdVideo-Key</c> khớp với <see cref="ApiKeyHash"/> của một dòng ở bảng này. Không có
/// đường nào khác để một request tự xưng là tenant nào đó.
/// </para>
/// <para>
/// <b>Sprint 1 cố ý giữ bảng này nhỏ.</b> Hạn mức, số dư, cấu hình riêng theo khách đều thuộc
/// Sprint 5–6 và có bảng riêng (<c>TenantQuota</c>, sổ credit). Nhét sẵn một cột
/// <c>credits_remaining</c> vào đây để "sau này dùng" là cách chắc chắn nhất để sáu tháng nữa
/// phải di dời dữ liệu có tranh chấp tiền bạc đi kèm.
/// </para>
/// </remarks>
public class Tenant : AuditableEntity, ISoftDelete
{
    /// <summary>Tên hiển thị, ví dụ "CHU Kafe". Chỉ để người vận hành nhận ra, không dùng để tra cứu.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Phần đầu không bí mật của API key, dùng để tra cứu và để ghi log.
    /// </summary>
    /// <remarks>
    /// Có index. Đây là thứ được phép xuất hiện trong log và trong màn hình quản trị — nó định
    /// danh được key mà không dùng được.
    /// </remarks>
    public required string ApiKeyPrefix { get; set; }

    /// <summary>
    /// SHA-256 của API key, dạng hex thường.
    /// </summary>
    /// <remarks>
    /// Key gốc chỉ tồn tại đúng một lần, lúc sinh ra, và được in ra màn hình cho người vận hành
    /// chép đi. Mất thì cấp key mới — hệ thống <b>không</b> có đường khôi phục, và đó là điểm mạnh
    /// chứ không phải thiếu sót: một DB dump bị lộ không cho ai gọi được API.
    /// </remarks>
    public required string ApiKeyHash { get; set; }

    /// <summary>Lần cấp lại key gần nhất. Null = key ban đầu.</summary>
    public DateTime? ApiKeyRotatedAt { get; set; }

    /// <summary>
    /// Tắt tenant mà không xoá dữ liệu.
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="IsDeleted"/> có chủ đích: tạm ngừng vì nợ cước khác hẳn với xoá theo
    /// yêu cầu của khách, và hai việc đó phải hoàn tác theo hai cách khác nhau.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>Ghi chú của người vận hành: vì sao tạo, vì sao tắt.</summary>
    public string? Note { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<AdVideoProject> Projects { get; set; } = [];
}
