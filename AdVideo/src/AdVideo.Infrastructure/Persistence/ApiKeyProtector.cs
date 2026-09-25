using Microsoft.AspNetCore.DataProtection;

namespace AdVideo.Infrastructure.Persistence;

/// <summary>
/// Ngoại lệ khi một secret trong DB không giải mã được.
/// </summary>
/// <remarks>
/// Tách riêng thay vì để <see cref="System.Security.Cryptography.CryptographicException"/> trần
/// lọt ra ngoài: lỗi trần chỉ nói "Key was invalid for use" và không gợi ý gì. Trong thực tế
/// nguyên nhân gần như luôn là MỘT trong ba thứ, và thông điệp phải kể ra cả ba để người vận
/// hành không phải đoán: đổi master key, restore DB từ máy khác sang, hoặc xoá thư mục key.
/// </remarks>
public sealed class AdVideoSecretException : Exception
{
    public AdVideoSecretException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Mã hoá/giải mã secret trước khi chạm DB (D10).</summary>
public interface IApiKeyProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}

/// <summary>
/// Cài đặt bằng ASP.NET Core Data Protection — cùng cơ chế NewsCMS đang dùng cho
/// <c>SiteApiKeyService</c>, nên không phải tự quản lý khoá đối xứng.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao Data Protection chứ không phải <c>Convert.ToBase64String</c>:</b> base64 không phải
/// mã hoá, nó chỉ là đổi cách viết. Ai đọc được DB là đọc được key provider — và key provider
/// tiêu tiền thật, nên đọc được DB nghĩa là tiêu được tiền của mình.
/// </para>
/// <para>
/// <b>Vì sao purpose string cố định:</b> Data Protection ràng buộc ciphertext với purpose. Đổi
/// purpose là mọi key trong DB thành vô dụng. Nên chuỗi này là một phần của schema, không phải
/// chi tiết cài đặt — đổi nó cần một migration dữ liệu.
/// </para>
/// </remarks>
public sealed class DataProtectionApiKeyProtector : IApiKeyProtector
{
    /// <summary>Purpose string. Xem remarks — đổi giá trị này là hỏng mọi secret đã lưu.</summary>
    public const string Purpose = "AdVideo.ProviderCredential.ApiKey";

    private readonly IDataProtector _protector;

    public DataProtectionApiKeyProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plainText)
    {
        // Chuỗi rỗng được giữ nguyên: credential chưa nạp key là trạng thái hợp lệ (seed trước,
        // nạp key sau bằng CLI), và mã hoá chuỗi rỗng sẽ khiến nó trông như "đã có key".
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText ?? string.Empty;
        }

        return _protector.Protect(plainText);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return protectedText ?? string.Empty;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new AdVideoSecretException(
                "Không giải mã được API key lưu trong bảng ProviderCredential bằng master key hiện tại. " +
                "Ba nguyên nhân thường gặp, kiểm tra theo thứ tự: " +
                "(1) khoá Data Protection đã bị đổi hoặc thư mục key bị xoá — khôi phục từ backup, " +
                "đường dẫn đọc từ cấu hình AdVideo:DataProtection:KeysPath; " +
                "(2) DB được restore từ một máy khác, nơi ciphertext sinh ra bằng master key của máy đó — " +
                "khi đó phải nạp lại key bằng lệnh set-credential; " +
                "(3) hai process đang chạy với hai thư mục key khác nhau — mọi instance phải dùng chung một đường dẫn.",
                ex);
        }
    }
}
