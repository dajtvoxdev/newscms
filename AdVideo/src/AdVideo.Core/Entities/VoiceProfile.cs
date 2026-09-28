using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

public enum VoiceProfileKind
{
    /// <summary>Giọng có sẵn, do người vận hành thêm. Mọi tenant thấy.</summary>
    Preset = 0,

    /// <summary>Giọng clone từ mẫu ghi âm của một tenant. Chỉ tenant đó thấy và dùng.</summary>
    Cloned = 1,
}

/// <summary>
/// Một giọng đọc chọn được khi tạo video: tên hiển thị + (engine TTS, voice id của engine đó).
/// </summary>
/// <remarks>
/// <para>
/// <b>Giọng gắn chặt với engine.</b> Voice id của ElevenLabs vô nghĩa với VieNeu. Chọn một giọng là
/// chọn luôn engine đọc — bước 4 dùng <see cref="Provider"/> của giọng thay vì engine theo tier.
/// </para>
/// <para>
/// <b>Giọng clone là dữ liệu nhạy cảm.</b> Giọng nói của một người thật dùng được để giả mạo họ. Vì
/// vậy: chỉ tenant tạo ra mới thấy (<see cref="TenantId"/>), lời xác nhận quyền dùng giọng được lưu
/// nguyên văn kèm người xác nhận và thời điểm, và mẫu ghi âm được giữ trong kho làm bằng chứng.
/// </para>
/// <para>
/// Không implement <c>ITenantScoped</c>: <see cref="TenantId"/> null là giọng có sẵn của hệ thống.
/// Global filter riêng: thấy giọng có sẵn + giọng của chính mình.
/// </para>
/// </remarks>
public class VoiceProfile : AuditableEntity, ISoftDelete
{
    /// <summary>Null = giọng có sẵn (preset). Có giá trị = giọng clone của tenant này.</summary>
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>Mô tả ngắn cho người chọn: giới tính, vùng miền, chất giọng.</summary>
    public string? Description { get; set; }

    /// <summary>Engine TTS sở hữu giọng, ví dụ <c>elevenlabs</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>Voice id bên engine.</summary>
    public required string ProviderVoiceId { get; set; }

    public VoiceProfileKind Kind { get; set; }

    /// <summary>URL nghe thử công khai do engine cung cấp (giọng có sẵn).</summary>
    public string? PreviewUrl { get; set; }

    /// <summary>Mẫu ghi âm đầu tiên trong bucket <c>adv-voice</c> (giọng clone) — vừa để nghe thử, vừa là bằng chứng.</summary>
    public string? SampleObjectKey { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Thứ tự hiện trong danh sách chọn; giọng có sẵn đầu tiên của một engine là giọng mặc định của engine đó.</summary>
    public int SortOrder { get; set; }

    /// <summary>Lời xác nhận có quyền dùng giọng — nguyên văn người dùng nhập (giọng clone).</summary>
    public string? ConsentStatement { get; set; }

    /// <summary>Người xác nhận (tên đăng nhập bên app gọi tới).</summary>
    public string? ConsentedBy { get; set; }

    public DateTime? ConsentedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
