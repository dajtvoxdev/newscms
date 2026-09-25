using System.ComponentModel.DataAnnotations;

namespace AdVideo.Infrastructure.Storage;

/// <summary>Chọn cách lưu file nhị phân.</summary>
public enum StorageProvider
{
    /// <summary>MinIO hoặc bất kỳ dịch vụ nào nói giao thức S3.</summary>
    Minio = 0,

    /// <summary>Thư mục trên đĩa. Chỉ dùng cho máy dev và test.</summary>
    LocalDisk = 1,
}

/// <summary>
/// Cấu hình tầng lưu trữ. Đây là một trong số ít thứ nằm ở config chứ không ở DB.
/// </summary>
/// <remarks>
/// D10 nói mọi cấu hình vận hành nằm trong DB, nhưng endpoint và khoá MinIO thì không thể: muốn
/// đọc DB đã phải có connection string, và muốn đọc file đã phải có endpoint lưu trữ. Đây là
/// tầng khởi động, dưới cả tầng cấu hình.
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "AdVideo:Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.LocalDisk;

    /// <summary>Endpoint MinIO mà ứng dụng gọi tới, ví dụ <c>http://localhost:9000</c>.</summary>
    public string ServiceUrl { get; set; } = "http://localhost:9000";

    /// <summary>
    /// Endpoint MÀ TRÌNH DUYỆT NGƯỜI DÙNG gọi tới, khi MinIO nằm sau nginx.
    /// </summary>
    /// <remarks>
    /// <b>Vì sao cần một endpoint riêng thay vì thay chuỗi trong URL:</b> chữ ký SigV4 ký cả
    /// header Host. Ký bằng <c>http://minio:9000</c> rồi đổi host thành <c>https://cdn.example.vn</c>
    /// là chữ ký hỏng, MinIO trả 403 SignatureDoesNotMatch. Nên URL tải về phải được KÝ bằng đúng
    /// host công khai — nghĩa là một client S3 thứ hai. Bỏ trống thì dùng <see cref="ServiceUrl"/>.
    /// </remarks>
    public string? PublicServiceUrl { get; set; }

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Vùng. MinIO không dùng tới nhưng AWS SDK bắt buộc phải có, nếu không sẽ ném lỗi lúc tạo client.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Thư mục gốc khi <see cref="Provider"/> là <see cref="StorageProvider.LocalDisk"/>.</summary>
    public string LocalRoot { get; set; } = "App_Data/advideo-storage";

    /// <summary>Kiểm tra cấu hình lúc khởi động, thay vì để lỗi nổ ở lần upload đầu tiên.</summary>
    public void Validate()
    {
        if (Provider == StorageProvider.LocalDisk)
        {
            if (string.IsNullOrWhiteSpace(LocalRoot))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:LocalRoot không được rỗng khi Provider = LocalDisk.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(ServiceUrl))
        {
            throw new InvalidOperationException($"{SectionName}:ServiceUrl không được rỗng khi Provider = Minio.");
        }

        if (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException(
                $"{SectionName}:AccessKey và SecretKey phải có giá trị khi Provider = Minio. " +
                "Đặt bằng biến môi trường hoặc user-secrets, đừng ghi vào appsettings.json.");
        }
    }
}
