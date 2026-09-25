using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdVideo.Infrastructure.Persistence;

/// <summary>
/// Tuỳ chọn JSON dùng cho cột JSON trong DB (<c>CapabilityJson</c>, <c>BriefJson</c>,
/// <c>RequestJson</c>).
/// </summary>
/// <remarks>
/// Tách ra một chỗ vì đây là định dạng của dữ liệu đã nằm trong DB: đọc và ghi mà dùng hai bộ
/// tuỳ chọn khác nhau thì manifest capability ghi hôm nay sẽ không đọc lại được ngày mai.
/// Enum ghi bằng TÊN chứ không phải số — dòng capability trong DB là thứ người vận hành sửa tay,
/// và <c>"Portrait9x16"</c> đọc được còn <c>0</c> thì phải tra bảng.
/// </remarks>
public static class AdVideoJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly JsonSerializerOptions Indented = new(Options)
    {
        WriteIndented = true,
    };
}
