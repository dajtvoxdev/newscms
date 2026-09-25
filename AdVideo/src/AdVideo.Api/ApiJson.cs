using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdVideo.Api;

/// <summary>
/// Định dạng JSON của API công khai.
/// </summary>
/// <remarks>
/// <para>
/// <b>snake_case</b> theo hợp đồng trong tài liệu thiết kế. Khác với <c>AdVideoJson</c> (camelCase)
/// vốn dùng cho cột JSON trong DB — hai bộ tuỳ chọn khác nhau vì chúng phục vụ hai người dùng
/// khác nhau: một bên là client của khách, một bên là người vận hành sửa DB bằng tay.
/// </para>
/// <para>
/// <b><see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>:</b> bộ mã hoá mặc định biến mọi
/// chữ có dấu thành <c>ê</c>, khiến một thông báo lỗi tiếng Việt trở thành một dòng không ai
/// đọc được khi xem bằng curl. "Unsafe" ở đây chỉ nói tới việc không escape ký tự có thể nguy hiểm
/// khi nhúng JSON thẳng vào HTML — API này trả <c>application/json</c>, không nhúng vào trang nào.
/// </para>
/// </remarks>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Áp cùng định dạng cho pipeline HTTP.</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = Canonical.PropertyNamingPolicy;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = Canonical.DefaultIgnoreCondition;
        options.Encoder = Canonical.Encoder;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}
