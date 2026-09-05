using NewsCMS.Application.Common;

namespace NewsCMS.Application.Content;

/// <summary>Kết quả sinh poster video: key lưu trữ + URL công khai.</summary>
public sealed record VideoPosterResult(string StorageKey, string PublicUrl);

/// <summary>
/// Sinh ảnh poster (JPEG 640px) từ file video bằng ffmpeg.
/// Trả về Failure khi ffmpeg chưa cấu hình hoặc lỗi — caller bỏ qua nhẹ nhàng,
/// poster là tính năng phụ, không được làm hỏng upload.
/// </summary>
public interface IVideoThumbnailService
{
    Task<Result<VideoPosterResult>> GenerateForVideoAsync(Stream videoStream, string originalFileName, CancellationToken ct);
}
