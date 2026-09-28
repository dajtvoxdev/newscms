using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

/// <summary>
/// Key quản trị toàn hệ thống — header <c>X-AdVideo-Operator-Key</c>, mở cửa <c>/v1/admin/*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bảng riêng, không phải một cờ trên <see cref="Tenant"/>.</b> Tenant là danh tính bị GIỚI HẠN
/// (global query filter ghim vào nó); operator là danh tính KHÔNG giới hạn. Gộp hai thứ vào một
/// bảng thì một cột boolean đặt sai là một khách đọc được sổ chi phí của mọi khách khác.
/// </para>
/// <para>
/// <b>Chỉ sinh được bằng CLI trên máy chủ</b> (<c>create-operator-key</c>). Endpoint sinh key quản
/// trị lại cần một key quản trị để gọi — vòng luẩn quẩn đó được cắt bằng quyền truy cập máy chủ.
/// Sau đó app (NewsCMS admin) giữ key này và làm mọi việc cấu hình còn lại qua API.
/// </para>
/// <para>
/// Thu hồi bằng <see cref="IsActive"/> = false, không xoá dòng: "key nào đã từng mở cửa quản trị,
/// ai tạo, lúc nào tắt" là thứ phải tra lại được sau một sự cố.
/// </para>
/// </remarks>
public class OperatorKey : AuditableEntity
{
    /// <summary>Tên để người vận hành nhận ra, ví dụ "NewsCMS admin — CHU Kafe".</summary>
    public required string Name { get; set; }

    /// <summary>Phần đầu không bí mật của key, dùng để tra cứu và ghi log. Xem <c>ApiKeyHasher</c>.</summary>
    public required string ApiKeyPrefix { get; set; }

    /// <summary>SHA-256 của key, hex thường. Key gốc chỉ in ra đúng một lần lúc sinh.</summary>
    public required string ApiKeyHash { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Lần cuối key này được dùng, làm tròn theo vài phút — để biết key nào đã chết mà chưa thu hồi.</summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? Note { get; set; }
}
