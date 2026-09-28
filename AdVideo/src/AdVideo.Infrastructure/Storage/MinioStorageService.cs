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

            // KHÔNG gán RegionEndpoint, kể cả = null: setter của nó xoá ServiceURL vừa đặt ở trên, và
            // client ném "No RegionEndpoint or ServiceURL configured" ngay lúc dựng — tức là mọi host
            // chạy Provider = Minio chết khi resolve IStorageService. Lỗi này nằm im cho tới khi có
            // test chạy trên MinIO thật (MinioStorageServiceTests); mọi test khác dùng LocalDisk.
            AuthenticationRegion = settings.Region,
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

        // Đo TRƯỚC khi gửi. Với UseChunkEncoding = false, SDK phải biết độ dài body — stream không
        // seek được thì nó cũng không gửi nổi, nên báo rõ ở đây thay vì để SDK ném một lỗi khó đọc.
        if (!content.CanSeek)
        {
            throw new ArgumentException(
                "Upload lên MinIO cần stream seek được (MemoryStream, FileStream). Ghi ra file tạm trước.", nameof(content));
        }

        long size = content.Length - content.Position;

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,

            // MinIO cũ không nhận aws-chunked encoding và trả về lỗi rất khó đọc
            // ("XAmzContentSHA256Mismatch"). Tắt đi thì SDK gửi body thẳng.
            UseChunkEncoding = false,

            // Mặc định SDK ĐÓNG stream sau khi gửi. Stream thuộc về bên gọi (họ đang bọc trong
            // using) — trước đây đọc lại độ dài sau khi gửi là ném ObjectDisposedException, tức
            // là mọi upload lên MinIO đều "thành công rồi nổ".
            AutoCloseStream = false,
        };

        PutObjectResponse response = await _client.PutObjectAsync(request, cancellationToken);

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
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, bucket))
        {
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

        await EnsureLifecycleAsync(bucket, cancellationToken);
    }

    /// <summary>Tên rule do hệ thống quản lý. Rule mang tên khác trên cùng bucket bị ghi đè — xem remarks.</summary>
    public const string LifecycleRuleId = "advideo-expire";

    /// <summary>
    /// Gắn rule tự xoá theo <see cref="Buckets.RetentionDays"/> — chạy MỖI lần khởi động, không chỉ lúc tạo bucket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trước đây hàm này chỉ tạo bucket, dù interface hứa "kèm lifecycle rule": <c>adv-work</c> không
    /// bao giờ được dọn. Chạy mỗi lần khởi động (chứ không chỉ khi bucket mới) để các bản cài đã có
    /// bucket từ trước cũng nhận được rule, và để đổi số ngày trong code là có hiệu lực ở lần deploy sau.
    /// </para>
    /// <para>
    /// <b>PUT lifecycle thay TOÀN BỘ cấu hình của bucket.</b> Ai đặt tay rule khác lên bucket có
    /// retention thì rule đó mất ở lần khởi động kế tiếp — rule của các bucket này thuộc về code.
    /// Bucket KHÔNG có retention thì không đụng tới: rule đặt tay ở đó (ví dụ tầng lạnh cho
    /// <c>adv-final</c>) được giữ nguyên.
    /// </para>
    /// </remarks>
    private async Task EnsureLifecycleAsync(string bucket, CancellationToken cancellationToken)
    {
        if (Buckets.RetentionDays(bucket) is not { } days)
        {
            return;
        }

        var configuration = new LifecycleConfiguration
        {
            Rules =
            [
                new LifecycleRule
                {
                    Id = LifecycleRuleId,
                    Status = LifecycleRuleStatus.Enabled,

                    // Tiền tố rỗng = cả bucket. Bucket này chỉ chứa file trung gian, không có ngoại lệ.
                    Filter = new LifecycleFilter { LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = string.Empty } },
                    Expiration = new LifecycleRuleExpiration { Days = days },
                },
            ],
        };

        try
        {
            await _client.PutLifecycleConfigurationAsync(
                new PutLifecycleConfigurationRequest { BucketName = bucket, Configuration = configuration },
                cancellationToken);

            _logger.LogInformation("Bucket {Bucket}: rule tự xoá sau {Days} ngày đã được áp.", bucket, days);
        }
        catch (AmazonS3Exception ex)
        {
            // Không làm chết tiến trình: thiếu rule dọn rác là chuyện tốn chỗ, không phải chuyện
            // hỏng dịch vụ. Nhưng phải kêu to — đây đúng là thứ từng bị quên lặng lẽ.
            _logger.LogError(
                ex,
                "Không áp được rule tự xoá {Days} ngày cho bucket {Bucket} ({Code}). File trung gian sẽ không được dọn — đặt tay bằng mc ilm.",
                days,
                bucket,
                ex.ErrorCode);
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
