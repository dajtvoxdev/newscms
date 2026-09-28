namespace AdVideo.Core.Media;

/// <summary>Định dạng ảnh sản phẩm nhận ra từ nội dung file.</summary>
public sealed record DetectedImage(string ContentType, string Extension);

/// <summary>
/// Nhận dạng ảnh sản phẩm bằng magic byte — không tin phần mở rộng hay Content-Type client gửi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao không tin header.</b> Client gửi <c>image/png</c> cho một file HTML là chuyện bình
/// thường (nhầm) và cũng là chuyện cố ý (tấn công): file đó nằm trong bucket, được ký URL và đưa cho
/// provider hoặc trình duyệt mở. Bốn byte đầu file thì không nói dối được.
/// </para>
/// <para>
/// Chỉ nhận JPEG, PNG, WebP: ba định dạng mà mọi provider video đang dùng đều đọc được. GIF động,
/// HEIC từ iPhone, SVG (có thể chứa script) bị từ chối ở cửa thay vì fail ở bước 6 sau khi đã trả
/// tiền giọng đọc.
/// </para>
/// </remarks>
public static class ProductImageFormat
{
    /// <summary>
    /// Trần một ảnh sản phẩm. Lớn hơn thì gần như chắc chắn là nhầm file, không phải ảnh sản phẩm.
    /// </summary>
    /// <remarks>Dùng chung cho upload qua API và ảnh tải về từ URL ở bước 1 — hai cửa, một giới hạn.</remarks>
    public const long MaxBytes = 15L * 1024 * 1024;

    /// <summary>Null = không phải JPEG/PNG/WebP.</summary>
    public static DetectedImage? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return new DetectedImage("image/jpeg", ".jpg");
        }

        if (data.Length >= 8 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new DetectedImage("image/png", ".png");
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return new DetectedImage("image/webp", ".webp");
        }

        return null;
    }
}
