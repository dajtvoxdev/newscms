using System.Text.Json;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Dựng <c>BriefJson</c> đúng hình dạng mà API ghi xuống và <c>JobBrief</c> đọc lên.
/// </summary>
/// <remarks>
/// Viết brief bằng chuỗi JSON thật chứ không bằng một đối tượng <c>JobBrief</c> dựng sẵn: chỗ hay
/// hỏng nhất của bước 1 là cái tên trường (<c>voice.script</c> chứ không phải <c>script</c>), và
/// dựng sẵn đối tượng thì đúng chỗ đó không được kiểm.
/// </remarks>
public static class TestBriefs
{
    /// <summary>URL ảnh sản phẩm mặc định — máy chủ ảnh giả trả lời mọi URL.</summary>
    public const string ProductImageUrl = "https://anh-gia.test/ca-phe.png";

    /// <summary>
    /// Lời thoại đủ dài để timeline phải chia thành nhiều shot.
    /// </summary>
    /// <remarks>
    /// Cần &gt; 10 giây vì bậc dài nhất của provider giả là 10 giây. Một shot thì không kiểm được
    /// <c>concat</c> — mà nối shot chính là chỗ filter graph hay sai nhất.
    /// </remarks>
    public const string LongScript =
        "Cà phê CHU rang mộc từ hạt Arabica Cầu Đất, thơm mùi ca cao và mật ong. " +
        "Mỗi mẻ rang chỉ mười hai ki-lô-gam, đóng túi ngay trong ngày để giữ trọn hương. " +
        "Pha phin hay pha máy đều đậm vị, hậu ngọt kéo dài. " +
        "Đặt hàng hôm nay để nhận ưu đãi giao tận nơi trong nội thành.";

    /// <summary>Lời thoại ngắn, gọn trong một shot — dùng cho test không cần kiểm việc nối shot.</summary>
    public const string ShortScript = "Cà phê CHU rang mộc, thơm mùi ca cao và mật ong.";

    /// <summary>Brief đầy đủ, hợp lệ.</summary>
    public static string Valid(
        string script = LongScript,
        IEnumerable<string>? productImages = null,
        string? nativeSound = "sfx_only") =>
        Build(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["brief"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["prompt"] = "Cận cảnh ly cà phê phin trên bàn gỗ, ánh sáng buổi sáng.",
                ["product_name"] = "Cà phê CHU",
            },
            ["voice"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["script"] = script,
                ["speed"] = 1.0,
            },
            ["assets"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["product_images"] = productImages?.ToArray() ?? [ProductImageUrl],
            },
            ["audio"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["native_sound"] = nativeSound,
            },
        });

    /// <summary>Brief thiếu lời thoại — bước 1 phải chặn.</summary>
    public static string WithoutScript() => Valid(script: string.Empty);

    /// <summary>Brief trỏ ảnh sản phẩm vào một URL không phải http/https.</summary>
    public static string WithLocalFileImage() =>
        Valid(productImages: ["file:///etc/passwd"]);

    private static string Build(Dictionary<string, object?> root) =>
        JsonSerializer.Serialize(root);
}
