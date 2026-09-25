using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using AdVideo.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Storage;

/// <summary>
/// Lưu trữ qua giao thức S3 — dùng với MinIO tự host.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>ForcePathStyle = true</c> là bắt buộc.</b> AWS SDK mặc định dựng URL kiểu virtual-host
/// (<c>https://ten-bucket.endpoint/key</c>). MinIO chạy trên IP hoặc hostname nội bộ thì không có
/// bản ghi DNS wildcard nào để phân giải tên đó, và lỗi hiện ra là "No such host is known" —
/// trông như mạng hỏng chứ không giống lỗi cấu hình.
/// </para>
/// <para>
/// <b>Hai client:</b> một để ứng dụng gọi (endpoint nội bộ), một chỉ để ký URL tải về (endpoint
/// công khai). Xem <see cref="StorageOptions.PublicServiceUrl"/> để biết vì sao không thể dùng
/// một client rồi thay chuỗi host.
/// </para>
/// </remarks>
public sealed class MinioStorageService : IStorageService, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly IAmazonS3 _signingClient;
    private readonly bool _signingClientIsSeparate;
    private readonly ILogger<MinioStorageService> _logger;

    public MinioStorageService(IOptions<StorageOptions> options, ILogger<MinioStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        StorageOptions settings = options.Value;
        settings.Validate();

        _logger = logger;
        _client = CreateClient(settings, settings.ServiceUrl);

        _signingClientIsSeparate = !string.IsNullOrWhiteSpace(settings.PublicServiceUrl)
            && !string.Equals(settings.PublicServiceUrl, settings.ServiceUrl, StringComparison.OrdinalIgnoreCase);

        _signingClient = _signingClientIsSeparate
            ? CreateClient(settings, settings.PublicServiceUrl!)
            : _client;
    }

    private static IAmazonS3 CreateClient(StorageOptions settings, string serviceUrl)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,

            // Xem phần remarks của lớp. Đây là dòng quan trọng nhất trong file.
            ForcePathStyle = true,

            AuthenticationRegion = settings.Region,
            RegionEndpoint = null,
        };

        var credentials = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);

        return new AmazonS3Client(credentials, config);
    }

    public async Task<StorageUploadResult> UploadAsync(
        string bucket,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,

            // MinIO cũ không nhận aws-chunked encoding và trả về lỗi rất khó đọc
            // ("XAmzContentSHA256Mismatch"). Tắt đi thì SDK gửi body thẳng.
            UseChunkEncoding = false,
        };

        PutObjectResponse response = await _client.PutObjectAsync(request, cancellationToken);

        // Đọc độ dài SAU khi upload: stream nào seek được thì Length đúng, stream không seek được
        // (ví dụ đầu ra của ffmpeg) thì Position sau khi đọc hết chính là số byte đã gửi.
        long size = content.CanSeek ? content.Length : content.Position;

        return new StorageUploadResult(bucket, objectKey, size, CleanETag(response.ETag));
    }

    public async Task<Stream> OpenReadAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        GetObjectResponse response = await _client.GetObjectAsync(bucket, objectKey, cancellationToken);

        // Trả thẳng ResponseStream và KHÔNG dispose response ở đây: dispose response là đóng luôn
        // stream vừa trả ra. Bên gọi dispose stream là đủ để trả kết nối về pool.
        return response.ResponseStream;
    }

    public async Task<byte[]> DownloadBytesAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        await using Stream stream = await OpenReadAsync(bucket, objectKey, cancellationToken);
        using var buffer = new MemoryStream();

        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    public Task<Uri> GetPresignedUrlAsync(
        string bucket,
        string objectKey,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Expires = DateTime.UtcNow.Add(lifetime),
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTP,
        };

        // Ký bằng client công khai. GetPreSignedURL là hàm đồng bộ — nó chỉ tính chữ ký tại chỗ,
        // không gọi mạng — nên không có gì để await ở đây.
        string url = _signingClient.GetPreSignedURL(request);

        return Task.FromResult(new Uri(url));
    }

    public async Task<bool> ExistsAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(bucket, objectKey, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Không có object là câu trả lời hợp lệ, không phải sự cố — nên không log ở mức lỗi.
            return false;
        }
    }

    public async Task DeleteAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(bucket, objectKey, cancellationToken);
    }

    public async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        if (await AmazonS3Util.DoesS3BucketExistV2Async(_client, bucket))
        {
            return;
        }

        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);

            _logger.LogInformation("Đã tạo bucket {Bucket}.", bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Api và Worker khởi động cùng lúc thì cả hai cùng thấy bucket chưa có rồi cùng tạo.
            // Thua cuộc đua này không phải lỗi: kết quả mong muốn đã đạt được.
            _logger.LogDebug("Bucket {Bucket} đã được tiến trình khác tạo trước.", bucket);
        }
    }

    private static string CleanETag(string? etag)
    {
        // S3 trả ETag có dấu ngoặc kép bao ngoài. Giữ nguyên thì mọi phép so sánh checksum về sau
        // đều lệch đúng hai ký tự.
        return etag?.Trim('"') ?? string.Empty;
    }

    public void Dispose()
    {
        _client.Dispose();

        if (_signingClientIsSeparate)
        {
            _signingClient.Dispose();
        }
    }
}
