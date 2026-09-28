using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Tạo video từ form VideoStudio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ảnh luôn đi qua <c>POST /v1/uploads</c></b>, kể cả ảnh đã có trong thư viện media: AdVideo
/// không gọi ngược vào NewsCMS để lấy ảnh (NewsCMS có thể chạy sau đăng nhập, hoặc trên máy mà
/// AdVideo không thấy), và ảnh trong kho AdVideo thì không chết khi ai đó xoá ảnh trong thư viện.
/// AdVideo tự nhận ra ảnh trùng, nên chọn cùng một ảnh cho nhiều video không nhân bản file.
/// </para>
/// <para>
/// Ảnh thư viện đọc qua global filter site: chọn id ảnh của site khác thì coi như không có.
/// </para>
/// </remarks>
public sealed class VideoStudioService : IVideoStudioService
{
    /// <summary>Ba định dạng AdVideo nhận — lọc sẵn để người dùng không chọn phải thứ sẽ bị từ chối.</summary>
    /// <remarks>List chứ không mảng: <c>mảng.Contains</c> trong LINQ bind sang overload span khi build bằng Roslyn mới, và EF không dịch được (xem global.json).</remarks>
    private static readonly List<string> AcceptedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Khớp trần của AdVideo (<c>CreateAdVideoRequestValidator.MaxProductImages</c>).</summary>
    public const int MaxImages = 10;

    private readonly AppDbContext _db;
    private readonly IFileStorage _files;
    private readonly IAdVideoClient _advideo;

    public VideoStudioService(AppDbContext db, IFileStorage files, IAdVideoClient advideo)
    {
        _db = db;
        _files = files;
        _advideo = advideo;
    }

    public async Task<IReadOnlyList<VideoStudioLibraryImage>> GetLibraryImagesAsync(int take = 24, CancellationToken ct = default)
    {
        var rows = await _db.Medias.AsNoTracking()
            .Where(m => m.Kind == "image" && AcceptedMimeTypes.Contains(m.MimeType))
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(m => new { m.Id, m.FilePath, m.FileName, m.Title })
            .ToListAsync(ct);

        return rows.Select(m => new VideoStudioLibraryImage(m.Id, m.FilePath, m.FileName, m.Title)).ToList();
    }

    public async Task<Result<AdVideoJobDto>> CreateAsync(VideoStudioCreateInput input, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Prompt))
        {
            errors.Add("Mô tả cảnh quay là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(input.Script))
        {
            errors.Add("Lời thoại là bắt buộc — AdVideo chưa tự viết lời.");
        }

        int imageCount = input.LibraryMediaIds.Count + input.Uploads.Count;

        if (imageCount == 0)
        {
            errors.Add("Chọn ít nhất một ảnh sản phẩm từ thư viện hoặc tải ảnh lên.");
        }
        else if (imageCount > MaxImages)
        {
            errors.Add($"Tối đa {MaxImages} ảnh.");
        }

        if (errors.Count > 0)
        {
            return Result<AdVideoJobDto>.Failure(string.Join(" ", errors));
        }

        var assetIds = new List<Guid>();

        if (input.LibraryMediaIds.Count > 0)
        {
            List<Guid> wanted = input.LibraryMediaIds.Distinct().ToList();

            var media = await _db.Medias.AsNoTracking()
                .Where(m => wanted.Contains(m.Id))
                .Select(m => new { m.Id, m.StorageKey, m.FileName, m.MimeType })
                .ToListAsync(ct);

            if (media.Count != wanted.Count)
            {
                return Result<AdVideoJobDto>.Failure("Có ảnh đã chọn không còn trong thư viện của site này. Tải lại trang rồi chọn lại.");
            }

            foreach (var m in media.OrderBy(m => wanted.IndexOf(m.Id)))
            {
                string path = _files.GetLocalPath(m.StorageKey);

                if (!File.Exists(path))
                {
                    return Result<AdVideoJobDto>.Failure($"Không đọc được file ảnh \"{m.FileName}\" trong thư viện.");
                }

                await using FileStream stream = File.OpenRead(path);
                Result<AdVideoUploadDto> uploaded = await _advideo.UploadImageAsync(stream, m.FileName, ct);

                if (!uploaded.Succeeded)
                {
                    return Result<AdVideoJobDto>.Failure($"Ảnh \"{m.FileName}\": {uploaded.Error}");
                }

                assetIds.Add(uploaded.Value!.AssetId);
            }
        }

        foreach (VideoStudioUpload file in input.Uploads)
        {
            await using Stream stream = file.Open();
            Result<AdVideoUploadDto> uploaded = await _advideo.UploadImageAsync(stream, file.FileName, ct);

            if (!uploaded.Succeeded)
            {
                return Result<AdVideoJobDto>.Failure($"Ảnh \"{file.FileName}\": {uploaded.Error}");
            }

            assetIds.Add(uploaded.Value!.AssetId);
        }

        return await _advideo.CreateJobAsync(new CreateAdVideoInput(
            input.IdempotencyKey,
            input.ProductName,
            input.Prompt,
            input.Script,
            input.DurationSeconds,
            input.AspectRatio,
            input.Quality,
            input.HasPerson,
            input.KeepSoundEffects ? "sfx_only" : "off",
            assetIds.Distinct().ToList(),
            input.VoiceId), ct);
    }
}
