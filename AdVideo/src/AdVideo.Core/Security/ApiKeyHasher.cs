using System.Security.Cryptography;
using System.Text;

namespace AdVideo.Core.Security;

/// <summary>
/// Sinh, băm và đối chiếu API key của tenant (header <c>X-AdVideo-Key</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao SHA-256 chứ không phải bcrypt/Argon2.</b> Hai loại đó sinh ra để chống dò mật khẩu
/// người đặt — vốn ngắn và đoán được. Key ở đây do hệ thống sinh, 256 bit ngẫu nhiên, nên không
/// có gì để dò. Đổi lại, key được kiểm ở <b>mọi</b> request: một hàm băm cố tình chậm sẽ biến
/// mỗi lần gọi API thành hàng chục mili giây CPU, và đó là một cách tự mở cửa cho tấn công làm
/// nghẽn hệ thống.
/// </para>
/// <para>
/// <b>Vì sao có <see cref="LookupPrefix"/>.</b> Tra DB bằng chính hash thì phải đánh index trên
/// một cột secret-đã-băm, và khi cần điều tra ("key nào đang gọi nhiều thế này?") thì không có
/// gì để gọi tên ngoài chuỗi băm. Prefix là phần <b>không bí mật</b> của key: đủ để tra index và
/// để ghi log, nhưng không đủ để dùng. Phần bí mật vẫn được đối chiếu bằng
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// </para>
/// </remarks>
public static class ApiKeyHasher
{
    /// <summary>Tiền tố cố định, để một key lọt ra ngoài thì nhìn là biết nó của hệ thống nào.</summary>
    public const string KeyPrefix = "adv_";

    /// <summary>Độ dài phần dùng để tra cứu, tính cả <see cref="KeyPrefix"/>.</summary>
    public const int LookupPrefixLength = 12;

    /// <summary>Số byte ngẫu nhiên trong phần bí mật.</summary>
    private const int SecretBytes = 32;

    /// <summary>Sinh một key mới. Đây là lần duy nhất giá trị này tồn tại ở dạng đọc được.</summary>
    public static string Generate()
    {
        // Base64Url: không có ký tự '+', '/', '=' nên copy-paste vào biến môi trường, URL hay
        // file YAML đều không bị biến dạng.
        string secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretBytes));

        return KeyPrefix + secret;
    }

    /// <summary>Băm key để lưu vào DB. Trả về chuỗi hex thường.</summary>
    public static string Hash(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Phần đầu của key, dùng để tra DB và để ghi log.
    /// </summary>
    /// <remarks>
    /// Key ngắn hơn <see cref="LookupPrefixLength"/> trả về nguyên văn: một key ngắn như vậy chắc
    /// chắn không khớp bản ghi nào, và cắt chuỗi quá tay chỉ đổi lỗi "sai key" thành lỗi crash.
    /// </remarks>
    public static string LookupPrefix(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return apiKey.Length <= LookupPrefixLength ? apiKey : apiKey[..LookupPrefixLength];
    }

    /// <summary>Đối chiếu key người gửi với hash trong DB, thời gian so sánh không phụ thuộc dữ liệu.</summary>
    public static bool Verify(string apiKey, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        byte[] incoming = Encoding.UTF8.GetBytes(Hash(apiKey));
        byte[] stored = Encoding.UTF8.GetBytes(storedHash);

        // FixedTimeEquals trả false ngay khi độ dài lệch, nên không cần (và không nên) kiểm tra
        // độ dài trước bằng một phép so sánh thường.
        return CryptographicOperations.FixedTimeEquals(incoming, stored);
    }

    /// <summary>Che key để hiện trong log hoặc UI: giữ prefix, giấu phần bí mật.</summary>
    public static string Mask(string apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? "(trống)" : LookupPrefix(apiKey) + "…";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
