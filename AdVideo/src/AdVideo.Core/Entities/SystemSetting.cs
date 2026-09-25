using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

/// <summary>
/// Cấu hình vận hành dạng key–value, đọc lúc chạy và cache ngắn.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lý do bảng này tồn tại ngay từ Sprint 1:</b> Sprint 1 đang chạy TRƯỚC Sprint 0 (chưa có
/// API key và ngân sách spike), nên mọi con số lẽ ra học được từ đo đạc — trần job đồng thời,
/// model id, timeout, trần chi tiêu — hiện là <b>giá trị bảo toàn chưa kiểm chứng</b>. Nằm trong
/// DB thì khi có số liệu thật chỉ sửa một dòng, không phải deploy lại.
/// </para>
/// <para>
/// Quy tắc: <b>không đoán rồi hard-code.</b> Nếu một con số chưa được đo, nó thuộc về bảng này.
/// </para>
/// </remarks>
public class SystemSetting : AuditableEntity
{
    /// <summary>Tên khoá, dạng <c>PascalCase</c>. Xem <see cref="SettingKeys"/>.</summary>
    public required string Key { get; set; }

    /// <summary>Giá trị, luôn lưu dạng chuỗi. Kiểu thật nằm ở <see cref="ValueType"/>.</summary>
    public required string Value { get; set; }

    public required SettingValueType ValueType { get; set; }

    /// <summary>Giải thích khoá này làm gì và tại sao mang giá trị hiện tại. Bắt buộc — setting không chú thích là setting không ai dám đổi.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// Giá trị này là ĐO ĐƯỢC hay ĐANG ĐOÁN. True = chưa kiểm chứng.
    /// </summary>
    /// <remarks>
    /// Để Sprint 0 chạy xong thì lọc <c>WHERE IsProvisional = 1</c> ra đúng danh sách những
    /// con số phải thay bằng số liệu thật. Không có cột này thì sẽ quên mất cái nào là đoán.
    /// </remarks>
    public bool IsProvisional { get; set; }

    /// <summary>Ngưỡng dưới cho validation. Null = không chặn.</summary>
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum SettingValueType
{
    Int = 0,
    Decimal = 1,
    Bool = 2,
    String = 3,
    Json = 4,
}

/// <summary>Tên các khoá setting đã biết. Hằng số để tránh gõ nhầm, không phải danh sách đóng.</summary>
public static class SettingKeys
{
    /// <summary>
    /// Số shot render đồng thời tối đa cho một job.
    /// </summary>
    /// <remarks>
    /// <b>Mặc định 1 là giá trị BẢO TOÀN, không phải giá trị đúng.</b> Con số thật phải đo bằng
    /// <c>spike/measure_ratelimit.py</c> (T0.6). Một video 30 giây = 4 shot: nếu key chỉ chịu được
    /// 2 request đồng thời thì "render song song" phải thành hàng đợi có giới hạn và thời gian job
    /// dài gấp đôi dự tính.
    /// </remarks>
    public const string MaxConcurrentShots = "MaxConcurrentShots";

    /// <summary>Provider video mặc định khi yêu cầu không chỉ định. Chốt chính thức sau Sprint 0.</summary>
    public const string DefaultVideoProvider = "DefaultVideoProvider";

    /// <summary>Engine TTS cho tier Nháp.</summary>
    public const string DraftTtsProvider = "DraftTtsProvider";

    /// <summary>Engine TTS cho tier Thành phẩm. Bắt buộc phải có mốc thời gian theo từ.</summary>
    public const string StandardTtsProvider = "StandardTtsProvider";

    /// <summary>Timeout một lần gọi video provider, giây.</summary>
    public const string VideoProviderTimeoutSeconds = "VideoProviderTimeoutSeconds";

    /// <summary>Số lần retry tối đa cho một shot, cùng provider, đổi seed.</summary>
    public const string ShotMaxRetries = "ShotMaxRetries";

    /// <summary>Trần chi tiêu đô la toàn hệ thống mỗi ngày. Vượt thì ngừng nhận job mới.</summary>
    public const string DailySystemCostLimitUsd = "DailySystemCostLimitUsd";

    /// <summary>Trần chi tiêu đô la mỗi job. Chặn trước khi tiêu, không phải sau.</summary>
    public const string MaxCostPerJobUsd = "MaxCostPerJobUsd";

    /// <summary>Hạn presigned URL tải về, phút.</summary>
    public const string DownloadUrlLifetimeMinutes = "DownloadUrlLifetimeMinutes";

    /// <summary>Chuẩn âm lượng đầu ra. Thiết kế chốt −14 LUFS.</summary>
    public const string TargetLoudnessLufs = "TargetLoudnessLufs";

    /// <summary>Dung sai lệch tiếng-hình tối đa, mili giây. Tiêu chí thành công S4: 200 ms.</summary>
    public const string MaxLipSyncDriftMs = "MaxLipSyncDriftMs";

    /// <summary>Code của <see cref="PromptTemplate"/> chứa negative prompt toàn cục.</summary>
    public const string GlobalNegativePromptCode = "GlobalNegativePromptCode";

    /// <summary>Bật smoke test provider hằng ngày (Sprint 5).</summary>
    public const string ProviderSmokeTestEnabled = "ProviderSmokeTestEnabled";
}
