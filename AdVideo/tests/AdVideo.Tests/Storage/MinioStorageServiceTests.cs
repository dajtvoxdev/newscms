using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AdVideo.Tests.Storage;

/// <summary>
/// Chạy test chỉ khi có MinIO thật — đặt <c>ADVIDEO_TEST_MINIO_URL</c> (ví dụ <c>http://127.0.0.1:9000</c>).
/// </summary>
/// <remarks>
/// Bỏ qua (Skip) thay vì fail khi thiếu: bắt cả bộ test phụ thuộc một container đang chạy là cách để
/// nó bị tắt đi. Nhưng bỏ qua phải HIỆN RA trong kết quả — đếm Skipped là thấy lớp storage production
/// hôm đó không được kiểm.
/// </remarks>
public sealed class MinioFactAttribute : FactAttribute
{
    public const string UrlVariable = "ADVIDEO_TEST_MINIO_URL";

    public MinioFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVariable)))
        {
            Skip = $"Không có MinIO thật — đặt {UrlVariable} (và ADVIDEO_TEST_MINIO_ACCESS_KEY / _SECRET_KEY nếu khác minioadmin).";
        }
    }
}

/// <summary>
/// <see cref="MinioStorageService"/> trên MinIO thật — lớp lưu trữ production mà 100% test khác đi vòng
/// qua (chúng dùng <c>LocalDiskStorageService</c>).
/// </summary>
public sealed class MinioStorageServiceTests : IDisposable
{
    private readonly StorageOptions _options = new()
    {
        Provider = StorageProvider.Minio,
        ServiceUrl = Environment.GetEnvironmentVariable(MinioFactAttribute.UrlVariable) ?? "http://127.0.0.1:9000",
        AccessKey = Environment.GetEnvironmentVariable("ADVIDEO_TEST_MINIO_ACCESS_KEY") ?? "minioadmin",
        SecretKey = Environment.GetEnvironmentVariable("ADVIDEO_TEST_MINIO_SECRET_KEY") ?? "minioadmin",
    };

    private readonly MinioStorageService _storage;

    public MinioStorageServiceTests()
    {
        _storage = new MinioStorageService(Options.Create(_options), NullLogger<MinioStorageService>.Instance);
    }

    public void Dispose() => _storage.Dispose();

    [MinioFact]
    public async Task Tao_du_bucket_hai_lan_khong_loi()
    {
        foreach (string bucket in Buckets.All)
        {
            await _storage.EnsureBucketAsync(bucket);
            await _storage.EnsureBucketAsync(bucket);
        }
    }

    [MinioFact]
    public async Task Ghi_doc_ky_URL_xoa_mot_object_that()
    {
        await _storage.EnsureBucketAsync(Buckets.Work);

        string key = $"test/{Guid.NewGuid():N}.bin";
        byte[] payload = [1, 2, 3, 4, 5, 250];

        using (var content = new MemoryStream(payload))
        {
            StorageUploadResult result = await _storage.UploadAsync(Buckets.Work, key, content, "application/octet-stream");

            result.SizeBytes.Should().Be(payload.Length);
            result.ETag.Should().NotStartWith("\"", "ETag phải được bỏ ngoặc kép");
        }

        (await _storage.ExistsAsync(Buckets.Work, key)).Should().BeTrue();
        (await _storage.DownloadBytesAsync(Buckets.Work, key)).Should().Equal(payload);

        // URL ký phải tải được bằng một client HTTP trơn — đúng như trình duyệt hay provider sẽ làm.
        Uri presigned = await _storage.GetPresignedUrlAsync(Buckets.Work, key, TimeSpan.FromMinutes(5));

        using (var http = new HttpClient())
        {
            (await http.GetByteArrayAsync(presigned)).Should().Equal(payload);
        }

        await _storage.DeleteAsync(Buckets.Work, key);
        (await _storage.ExistsAsync(Buckets.Work, key)).Should().BeFalse();
    }

    [MinioFact]
    public async Task Bucket_trung_gian_co_rule_tu_xoa_7_ngay_bucket_video_khach_thi_khong()
    {
        foreach (string bucket in Buckets.All)
        {
            await _storage.EnsureBucketAsync(bucket);
        }

        using IAmazonS3 admin = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config { ServiceURL = _options.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = _options.Region });

        GetLifecycleConfigurationResponse work = await admin.GetLifecycleConfigurationAsync(Buckets.Work);

        LifecycleRule rule = work.Configuration.Rules.Should().ContainSingle(r => r.Id == MinioStorageService.LifecycleRuleId).Which;
        rule.Status.Should().Be(LifecycleRuleStatus.Enabled);
        rule.Expiration.Days.Should().Be(7);

        foreach (string bucket in Buckets.All.Where(b => b != Buckets.Work))
        {
            try
            {
                GetLifecycleConfigurationResponse other = await admin.GetLifecycleConfigurationAsync(bucket);

                other.Configuration?.Rules?.Should().NotContain(
                    r => r.Id == MinioStorageService.LifecycleRuleId,
                    $"rule xoá trên {bucket} là xoá dữ liệu phải giữ");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Không có cấu hình lifecycle nào — đúng như mong đợi.
            }
        }
    }
}
