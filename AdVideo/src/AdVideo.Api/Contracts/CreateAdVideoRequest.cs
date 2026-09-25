namespace AdVideo.Api.Contracts;

/// <summary>
/// Thân request của <c>POST /v1/ad-videos</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mọi thuộc tính đều nullable, kể cả những cái bắt buộc.</b> Nếu khai <c>required</c> thì
/// thiếu trường sẽ ném ra từ tầng deserialize, và khách nhận về một thông báo do System.Text.Json
/// viết — bằng tiếng Anh, chỉ rõ tên thuộc tính C#, không nói được phải sửa gì. Nhận null rồi tự
/// kiểm tra ở <see cref="CreateAdVideoRequestValidator"/> cho phép trả về một danh sách lỗi
/// đọc được, và trả hết một lượt thay vì bắt khách sửa từng cái một.
/// </para>
/// <para>
/// Hợp đồng JSON theo tài liệu thiết kế (snake_case). Sprint 1 chỉ dùng phần tối thiểu; các
/// trường còn lại vẫn được nhận và lưu vào brief gốc để không phá hợp đồng khi sprint sau bật.
/// </para>
/// </remarks>
public sealed class CreateAdVideoRequest
{
    public BriefDto? Brief { get; set; }
    public AssetsDto? Assets { get; set; }
    public VoiceDto? Voice { get; set; }
    public AudioDto? Audio { get; set; }
    public CaptionsDto? Captions { get; set; }
    public OptionsDto? Options { get; set; }

    /// <summary>URL nhận webhook. Sprint 1 lưu lại nhưng chưa gửi.</summary>
    public string? CallbackUrl { get; set; }
}

public sealed class BriefDto
{
    public string? ProductName { get; set; }

    /// <summary>Bắt buộc. Mô tả bằng tiếng Việt thường, không phải prompt kỹ thuật.</summary>
    public string? Prompt { get; set; }

    /// <summary>6–180 giây. Hệ thống tự chia shot.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>"9:16" (mặc định) · "1:1" · "16:9". Một job một tỉ lệ.</summary>
    public string? AspectRatio { get; set; }

    /// <summary>Slug định dạng trong kho prompt. Sprint 2 mới dùng.</summary>
    public string? Format { get; set; }

    public string? Language { get; set; }
}

public sealed class AssetsDto
{
    /// <summary>Bắt buộc ít nhất một ảnh sản phẩm.</summary>
    public IList<string>? ProductImages { get; set; }

    public TalentDto? Talent { get; set; }

    public IList<string>? SceneReference { get; set; }

    /// <summary>Sprint 5.</summary>
    public string? BrandKitId { get; set; }
}

/// <summary>Người xuất hiện trong video.</summary>
/// <remarks>
/// <see cref="ConsentRef"/> bắt buộc khi có <see cref="ImageUrl"/>, và đây không phải thủ tục
/// hình thức: dùng hình ảnh một người thật mà không có bằng chứng được phép là rủi ro pháp lý
/// thuộc về bên vận hành (R5), không chuyển sang khách bằng điều khoản được.
/// </remarks>
public sealed class TalentDto
{
    public string? ImageUrl { get; set; }
    public string? ConsentRef { get; set; }
}

public sealed class VoiceDto
{
    /// <summary>Id giọng nội bộ, KHÔNG phải voice_id của ElevenLabs.</summary>
    public string? VoiceProfileId { get; set; }

    public double? Speed { get; set; }

    /// <summary>
    /// Lời thoại. Sprint 1 <b>bắt buộc</b> vì chưa có LLM đạo diễn viết hộ (bước 2 thuộc Sprint 2).
    /// </summary>
    public string? Script { get; set; }
}

public sealed class AudioDto
{
    /// <summary>"off" (mặc định) · "sfx_only" · "full".</summary>
    public string? NativeSound { get; set; }

    /// <summary>Sprint 2. Nhận nhưng chưa dùng.</summary>
    public MusicDto? Music { get; set; }
}

public sealed class MusicDto
{
    public string? Mode { get; set; }
    public string? Mood { get; set; }
}

public sealed class CaptionsDto
{
    public bool? Enabled { get; set; }
    public string? Style { get; set; }
}

public sealed class OptionsDto
{
    /// <summary>"draft" · "standard" (mặc định) · "premium".</summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Ép provider cụ thể. Bỏ trống là đúng trong hầu hết trường hợp.
    /// </summary>
    /// <remarks>
    /// Tồn tại để chẩn đoán và để so sánh chất lượng, không phải để khách dùng hằng ngày —
    /// Luật 3: đừng bắt người làm marketing phải biết Veo là gì.
    /// </remarks>
    public string? Provider { get; set; }

    /// <summary>
    /// Trần chi tiêu cứng cho job này, đơn vị đô la.
    /// </summary>
    /// <remarks>
    /// Thiết kế gọi trường này là <c>max_credits</c>; hệ thống credit bị hoãn nên Sprint 1 dùng
    /// thẳng đô la. Vẫn bị kẹp bởi trần toàn hệ thống <c>MaxCostPerJobUsd</c>: khách hạ trần
    /// xuống được, nâng lên thì không.
    /// </remarks>
    public decimal? MaxCostUsd { get; set; }

    /// <summary>Sprint 2. Sprint 1 luôn coi như false vì chưa có bước duyệt storyboard.</summary>
    public bool? RequireStoryboardApproval { get; set; }

    /// <summary>Video có người xuất hiện không. Cùng với <see cref="Quality"/> là hai thứ khách thật sự chọn.</summary>
    public bool? HasPerson { get; set; }
}
