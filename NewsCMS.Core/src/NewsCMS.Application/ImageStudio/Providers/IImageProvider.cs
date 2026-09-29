using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio.Providers;

/// <summary>
/// Một adapter nói chuyện với một kiểu API tạo ảnh. <b>Mỗi lần gọi sinh đúng một ảnh</b>: nhiều biến
/// thể thì runner gọi song song nhiều lần. Không phải gateway nào cũng nhận <c>n &gt; 1</c>, và mỗi lần
/// gọi có một dòng sổ chi phí riêng — một biến thể lỗi không làm mất các biến thể còn lại.
/// </summary>
/// <remarks>
/// Adapter <b>luôn trả bytes</b>. Provider trả URL thì adapter tự tải về qua lớp chặn SSRF — phía sau
/// không bao giờ phải tin một URL do bên ngoài đưa.
/// </remarks>
public interface IImageProvider
{
    ImageProviderAdapter Adapter { get; }

    Task<ImageProviderResult> GenerateAsync(ImageGenerateRequest request, CancellationToken ct = default);
}

public interface IImageProviderRegistry
{
    /// <summary>Null khi adapter chưa có code, hoặc là Fake mà cấu hình không bật.</summary>
    IImageProvider? Get(ImageProviderAdapter adapter);

    IReadOnlyList<ImageProviderAdapter> Available { get; }
}

public sealed record ImageData(byte[] Bytes, string MimeType);

/// <summary>Thông tin gọi một model: lấy từ <c>ImageModel</c> + key đã giải mã của kết nối.</summary>
public sealed record ImageProviderContext(
    string BaseUrl,
    string ApiKey,
    string ModelId,
    string? Quality,
    string OutputFormat,
    int TimeoutSeconds,
    string? ExtraParamsJson);

public sealed record ImageGenerateRequest(ImageProviderContext Context, string Prompt, string Size);

public sealed record ImageProviderResult(
    bool Ok,
    ImageData? Image,
    int? HttpStatus,
    string? ErrorCode,
    string? ErrorMessage,
    int DurationMs)
{
    public static ImageProviderResult Success(ImageData image, int? httpStatus, int durationMs) =>
        new(true, image, httpStatus, null, null, durationMs);

    public static ImageProviderResult Failure(string errorCode, string message, int? httpStatus, int durationMs) =>
        new(false, null, httpStatus, errorCode, message, durationMs);
}

/// <summary>Mã lỗi chuẩn hoá giữa các provider — quyết định có thử lại hay không.</summary>
public static class ImageProviderErrorCodes
{
    public const string ContentPolicy = "content_policy";
    public const string RateLimited = "rate_limited";
    public const string Timeout = "timeout";
    public const string BadRequest = "bad_request";
    public const string Auth = "auth";
    public const string ProviderError = "provider_error";
    public const string InvalidResponse = "invalid_response";
}
