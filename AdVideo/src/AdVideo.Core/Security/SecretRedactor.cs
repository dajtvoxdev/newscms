using System.Text.RegularExpressions;

namespace AdVideo.Core.Security;

/// <summary>
/// Che key trước khi một chuỗi đi vào log, DB hoặc API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao cần:</b> nhiều API dội lại request đã gửi trong thông báo lỗi, và toàn bộ thân phản hồi
/// lỗi được ghi nguyên văn vào <c>ProviderCall.RawError</c> và <c>AdVideoJob.RawProviderError</c>.
/// Một lỗi 400 của provider là đủ để key nằm trong DB dưới dạng chữ thường.
/// </para>
/// <para>Hai lớp:</para>
/// <list type="number">
/// <item><see cref="RedactKnown"/> — nơi biết key (adapter, engine descriptor) thay đúng chuỗi key
/// bằng dạng che <c>****abcd</c>. Chắc chắn nhất.</item>
/// <item><see cref="RedactPatterns"/> — nơi không biết key (ghi DB, bắt exception) che mọi token có
/// hình dạng key đã biết. Lưới an toàn, không thay được lớp 1.</item>
/// </list>
/// </remarks>
public static partial class SecretRedactor
{
    public const string Mask = "****";

    /// <summary>Key ngắn hơn ngần này thì không thay: thay "abc" là phá nát cả chuỗi lỗi mà không che được gì.</summary>
    public const int MinSecretLength = 8;

    [GeneratedRegex(@"^(sk[-_][A-Za-z0-9_-]{16,}|xi-[A-Za-z0-9]{20,}|[0-9a-fA-F]{32,}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\..*)$")]
    private static partial Regex KnownKeyToken();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{32,}$")]
    private static partial Regex LongOpaqueToken();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(sk[-_][A-Za-z0-9_-]{16,}|xi-[A-Za-z0-9]{20,}|[0-9a-fA-F]{32,}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*)(?![A-Za-z0-9_-])")]
    private static partial Regex KeyInText();

    /// <summary>Dạng che của một key: <c>****</c> + 4 ký tự cuối. Khớp <c>ResolvedCredential.MaskedKey</c>.</summary>
    public static string MaskOf(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Length <= 4 ? Mask : Mask + secret[^4..];
    }

    /// <summary>Thay mọi lần xuất hiện của key (cả dạng urlencode) bằng dạng che.</summary>
    public static string? RedactKnown(string? text, string? secret)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength)
        {
            return text;
        }

        string masked = MaskOf(secret);
        string result = text.Replace(secret, masked, StringComparison.Ordinal);
        string encoded = Uri.EscapeDataString(secret);

        return encoded == secret ? result : result.Replace(encoded, masked, StringComparison.Ordinal);
    }

    /// <summary>Che mọi token có hình dạng key đã biết (<c>sk-…</c>, <c>xi-…</c>, hex ≥ 32, JWT).</summary>
    public static string? RedactPatterns(string? text) =>
        string.IsNullOrEmpty(text) ? text : KeyInText().Replace(text, Mask);

    /// <summary>
    /// Chuỗi có chứa token trông như key thật không — luật chặt hơn <see cref="RedactPatterns"/>,
    /// dùng để TỪ CHỐI LƯU descriptor, nơi báo nhầm chỉ tốn một lần sửa chứ không làm mất dữ liệu.
    /// </summary>
    public static bool LooksLikeSecret(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (string token in text.Split([' ', '\t', '\n', '\r', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (KnownKeyToken().IsMatch(token))
            {
                return true;
            }

            if (LongOpaqueToken().IsMatch(token) && token.Any(char.IsDigit) && token.Any(char.IsLetter))
            {
                return true;
            }
        }

        return false;
    }
}
