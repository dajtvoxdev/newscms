using System.Text.RegularExpressions;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Vệ sĩ cho các prop CSS tự do của khối động.
///
/// Vấn đề: nhiều khối nhận prop dạng chuỗi rồi đổ thẳng vào <c>style="..."</c>
/// (vd <c>color</c>, <c>maxHeight</c>). Người soạn trang có quyền <c>Builder.Code.Manage</c>
/// thì có thể viết CSS tay, nhưng prop khối thì KHÔNG đòi quyền đó — nếu không lọc, giá trị
/// prop là một vector tiêm CSS vào HTML công khai.
///
/// Lọc bằng allowlist ký tự: chỉ nhận ký tự an toàn cho giá trị CSS, chặn dấu nháy (thoát khỏi
/// attribute), dấu chấm-phẩy (kết thúc declaration), ngoặc nhọn (khối CSS mới), và cặp
/// <c>/*</c> <c>*/</c> (comment để ăn phần còn lại). <c>url()</c> không nằm trong allowlist nên
/// bị chặn tự động.
/// </summary>
internal static class BlockCss
{
    /// <summary>
    /// Regex "giá trị CSS hợp lệ": chữ cái, số, dấu cách, dấu gạch, gạch dưới, dấu chấm, dấu phẩy,
    /// dấu hai chấm, dấu phần trăm, dấu hoa thị, dấu ngã, dấu cộng, dấu ngoặc đơn (cho calc/var),
    /// dấu nháy KÉP chỉ cho chuỗi, dấu hash (màu hex), dấu bằng, dấu /, dấu >, dấu ^.
    /// KHÔNG có nháy đơn, chấm-phẩy, ngoặc nhọn, < >.
    /// </summary>
    private static readonly Regex SafeValue = new(
        @"^[A-Za-z0-9\s\-_.,:%*~+()#/>^]+$", RegexOptions.Compiled);

    /// <summary>Giá trị này có an toàn để đổ vào một declaration CSS không (vd "red", "12px", "var(--color-brand-500)").</summary>
    public static bool IsSafeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        // Cho phép var(--x)/calc(): "v" có "(" ")" "-" trong allowlist. Chấm-dấu-phẩy, nháy đơn,
        // ngoặc nhọn và cặp comment là những thứ tạo vector thoát — không có trong allowlist.
        return v.Length <= 200
            && !v.Contains("/*")
            && !v.Contains("*/")
            && !v.Contains("url", StringComparison.OrdinalIgnoreCase)
            && !v.Contains("expression", StringComparison.OrdinalIgnoreCase)
            && !v.Contains("import", StringComparison.OrdinalIgnoreCase)
            && SafeValue.IsMatch(v);
    }

    /// <summary>
    /// Trả giá trị nếu an toàn, ngược lại trả null — caller tự chọn fallback mặc định.
    /// Dùng cho prop màu chữ, kích thước… đổ vào style.
    /// </summary>
    public static string? Safe(string? value) => IsSafeValue(value) ? value!.Trim() : null;
}
