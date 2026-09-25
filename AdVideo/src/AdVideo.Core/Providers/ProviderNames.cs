namespace AdVideo.Core.Providers;

/// <summary>
/// Danh tính provider là <b>chuỗi</b>, không phải enum.
/// </summary>
/// <remarks>
/// Đây là hệ quả trực tiếp của quyết định D2 ("provider là thứ có thể thay"). Nếu danh tính
/// provider là enum thì mỗi lần thêm provider phải sửa code + tạo migration, đúng vào lúc
/// khẩn nhất — khi một provider vừa chết và cần thay ngay. Sora bị gỡ khỏi API trong chưa
/// đầy một năm kể từ lúc ra mắt là bằng chứng thực tế, không phải giả định.
///
/// Hằng số ở đây chỉ để tránh gõ nhầm chuỗi, KHÔNG phải danh sách đóng: adapter mới chỉ cần
/// một dòng <see cref="Entities.ProviderCredential"/> trong DB là chạy được.
/// </remarks>
public static class ProviderNames
{
    public const string Veo = "veo";
    public const string Kling = "kling";
    public const string Seedance = "seedance";
    public const string Vidu = "vidu";

    /// <summary>Đã thiết kế chỗ cắm, chưa viết adapter.</summary>
    public const string Runway = "runway";

    public const string ElevenLabs = "elevenlabs";
    public const string VieNeu = "vieneu";

    /// <summary>Provider giả cho test và cho Sprint 1 chạy khi chưa có API key. Không được đăng ký ở production.</summary>
    public const string Fake = "fake";
}

/// <summary>Provider thuộc loại nào. Một provider chỉ thuộc MỘT loại.</summary>
public enum ProviderCategory
{
    Video = 0,
    TextToSpeech = 1,
    LipSync = 2,
}
