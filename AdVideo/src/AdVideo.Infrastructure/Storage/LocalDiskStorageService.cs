using System.Security.Cryptography;
using AdVideo.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Storage;

/// <summary>
/// Lưu file vào thư mục trên đĩa. Dành cho máy dev và test — không dùng khi chạy thật.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao có cài đặt này:</b> để chạy được toàn tuyến pipeline mà không cần dựng MinIO. Sprint 1
/// phải chứng minh được xương sống chạy thông trước khi thêm một dịch vụ hạ tầng nữa vào.
/// </para>
/// <para>
/// <b>Vì sao không dùng khi chạy thật:</b> hai tiến trình (Api và Worker) chỉ dùng chung được
/// thư mục này khi chúng ở trên cùng một máy. Ngày tách Worker ra máy riêng, mọi thứ vẫn "chạy"
/// nhưng Worker ghi file vào đĩa của nó còn Api tìm file trên đĩa của mình.
/// </para>
/// </remarks>
public sealed class LocalDiskStorageService : IStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalDiskStorageService> _logger;

    public LocalDiskStorageService(IOptions<StorageOptions> options, ILogger<LocalDiskStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        StorageOptions settings = options.Value;
        settings.Validate();

        _root = Path.GetFullPath(settings.LocalRoot);
        _logger = logger;

        Directory.CreateDirectory(_root);
    }

    public async Task<StorageUploadResult> UploadAsync(
        string bucket,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = ResolvePath(bucket, objectKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Ghi ra file tạm rồi mới đổi tên: tiến trình chết giữa chừng thì để lại file .tmp chứ
        // không để lại một object đọc được nhưng thiếu đuôi. File cụt nguy hiểm hơn file thiếu,
        // vì ffprobe vẫn mở được nó và bước sau tưởng là dữ liệu thật.
        string tempPath = path + ".tmp";
        long size;

        await using (FileStream file = File.Create(tempPath))
        {
            await content.CopyToAsync(file, cancellationToken);
            size = file.Length;
        }

        File.Move(tempPath, path, overwrite: true);

        return new StorageUploadResult(bucket, objectKey, size, await ComputeTagAsync(path, cancellationToken));
    }

    public Task<Stream> OpenReadAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(bucket, objectKey);

        Stream stream = File.OpenRead(path);

        return Task.FromResult(stream);
    }

    public async Task<byte[]> DownloadBytesAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        return await File.ReadAllBytesAsync(ResolvePath(bucket, objectKey), cancellationToken);
    }

    /// <remarks>
    /// Trả về đường dẫn <c>file://</c>, KHÔNG có hạn dùng và KHÔNG có chữ ký. Đây là lý do cài đặt
    /// này chỉ dùng cho dev: ai cầm đường dẫn cũng mở được, và nó chỉ mở được trên chính máy đó.
    /// </remarks>
    public Task<Uri> GetPresignedUrlAsync(
        string bucket,
        string objectKey,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Uri(ResolvePath(bucket, objectKey)));
    }

    public Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(ResolvePath(bucket, objectKey)));

    public Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(bucket, objectKey);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(_root, SanitizeSegment(bucket)));

        return Task.CompletedTask;
    }

    private string ResolvePath(string bucket, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("objectKey không được rỗng.", nameof(objectKey));
        }

        string combined = Path.Combine(_root, SanitizeSegment(bucket), objectKey.Replace('/', Path.DirectorySeparatorChar));
        string full = Path.GetFullPath(combined);

        // Chặn thoát thư mục gốc. Object key có phần do người dùng đặt (tên file upload), và
        // "../../appsettings.json" là một tên file hợp lệ với hệ thống tập tin.
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Object key {ObjectKey} thoát khỏi thư mục gốc — từ chối.", objectKey);

            throw new UnauthorizedAccessException($"Object key không hợp lệ: {objectKey}");
        }

        return full;
    }

    private static string SanitizeSegment(string segment)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            segment = segment.Replace(invalid, '_');
        }

        return segment;
    }

    /// <remarks>
    /// Đây là SHA-256, không phải ETag kiểu S3 (vốn là MD5). Cố ý khác nhau để không ai so sánh
    /// ETag của hai cài đặt lưu trữ với nhau rồi kết luận file khác nhau.
    /// </remarks>
    private static async Task<string> ComputeTagAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(file, cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
