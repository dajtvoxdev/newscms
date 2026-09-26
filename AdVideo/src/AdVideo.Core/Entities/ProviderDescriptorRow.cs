using AdVideo.Core.Common;
using AdVideo.Core.Providers.Descriptors;

namespace AdVideo.Core.Entities;

/// <summary>
/// Một phiên bản descriptor của provider khai báo (xem <see cref="ProviderDescriptor"/>).
/// </summary>
/// <remarks>
/// <para>
/// Theo khuôn <see cref="PromptTemplate"/> (<c>Code</c> + <c>Version</c> + <c>IsActive</c>) — khuôn
/// versioning/rollback duy nhất đã có trong repo. <b>Bản cũ không bao giờ bị sửa đè</b>: khi một
/// clip hỏng, câu hỏi đầu tiên là "descriptor nào đã sinh ra nó", và
/// <see cref="ProviderCall.DescriptorSha256"/> trỏ về đúng một dòng ở đây.
/// </para>
/// <para>
/// <b>Bản mới luôn vào ở trạng thái TẮT</b> và chỉ được bật bằng một lệnh riêng sau khi chạy khô
/// xanh. Thêm là soạn thảo; bật là quyết định cho chạy vào job thật đang tiêu tiền.
/// </para>
/// </remarks>
public class ProviderDescriptorRow : AuditableEntity, ISoftDelete
{
    /// <summary>Tên provider, bằng <see cref="ProviderDescriptor.Name"/> và <c>ProviderCredential.Provider</c>.</summary>
    public required string Code { get; set; }

    public required int Version { get; set; }

    public required DescriptorKind Kind { get; set; }

    /// <summary>Nguyên văn JSON người vận hành nạp vào, kể cả comment. Là nguồn sự thật, không phải bản đã chuẩn hoá.</summary>
    public required string Json { get; set; }

    /// <summary>SHA-256 (hex thường) của <see cref="Json"/>. Ghi kèm mỗi <see cref="ProviderCall"/> để tra ngược.</summary>
    public required string Sha256 { get; set; }

    public bool IsActive { get; set; }

    public string? ChangeNote { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
