using NewsCMS.Application.Common;

namespace NewsCMS.Application.VideoStudio;

/// <summary>Ảnh trong thư viện media của site, chọn được làm ảnh sản phẩm.</summary>
public record VideoStudioLibraryImage(Guid MediaId, string Url, string FileName, string? Title);

/// <summary>Một file người dùng vừa tải lên form tạo video.</summary>
public sealed record VideoStudioUpload(string FileName, Func<Stream> Open);

/// <summary>Brief tạo video từ form VideoStudio.</summary>
public record VideoStudioCreateInput(
    string IdempotencyKey,
    string? ProductName,
    string Prompt,
    string Script,
    int DurationSeconds,
    string AspectRatio,
    string Quality,
    bool HasPerson,
    bool KeepSoundEffects,
    IReadOnlyList<Guid> LibraryMediaIds,
    IReadOnlyList<VideoStudioUpload> Uploads,
    Guid? VoiceId = null);

/// <summary>
/// Trang VideoStudio của một site: gom ảnh (thư viện + tải lên), đẩy sang AdVideo, tạo job.
/// </summary>
public interface IVideoStudioService
{
    /// <summary>Ảnh JPEG/PNG/WebP mới nhất trong thư viện media của site hiện tại.</summary>
    Task<IReadOnlyList<VideoStudioLibraryImage>> GetLibraryImagesAsync(int take = 24, CancellationToken ct = default);

    Task<Result<AdVideoJobDto>> CreateAsync(VideoStudioCreateInput input, CancellationToken ct = default);
}
