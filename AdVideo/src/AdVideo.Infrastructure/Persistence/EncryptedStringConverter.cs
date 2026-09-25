using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdVideo.Infrastructure.Persistence;

/// <summary>
/// Chuyển <c>string</c> dạng plaintext ↔ ciphertext cho cột <c>EncryptedApiKey</c>, để trong DB
/// chỉ có ciphertext (D10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Ràng buộc về lifetime — đọc trước khi đổi cách đăng ký DI:</b> EF Core cache model theo
/// kiểu <see cref="AdVideoDbContext"/>, và instance converter này bị "nướng" vào model đã cache.
/// Nghĩa là <see cref="IApiKeyProtector"/> bắt buộc phải là SINGLETON. Nếu đăng ký nó theo scoped
/// thì DbContext đầu tiên được tạo sẽ khoá protector của nó vào model, và mọi DbContext sau đó
/// dùng lại protector cũ — bug không biểu hiện ngay, chỉ hiện khi đổi cách tạo protector.
/// </para>
/// <para>
/// <b>Vì sao là <see cref="ValueConverter"/> chứ không mã hoá tay trong store:</b> mã hoá tay thì
/// mọi chỗ đọc/ghi <see cref="Core.Entities.ProviderCredential"/> đều phải nhớ mã hoá/giải mã,
/// và quên một chỗ là key plaintext nằm trong DB. Converter làm ở tầng EF nên không có đường
/// nào ghi plaintext được, kể cả khi ai đó dùng <c>DbContext</c> trực tiếp thay vì qua store.
/// </para>
/// </remarks>
public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    public EncryptedStringConverter(IApiKeyProtector protector)
        : base(
            v => protector.Protect(v),
            v => protector.Unprotect(v))
    {
        ArgumentNullException.ThrowIfNull(protector);
    }
}
