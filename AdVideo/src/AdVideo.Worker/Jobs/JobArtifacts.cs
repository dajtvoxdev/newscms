using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Security;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace AdVideo.Worker.Jobs;

/// <summary>
/// Hai việc mà bước nào cũng phải làm: cất file vào object storage và ghi lại một lời gọi ra ngoài.
/// </summary>
/// <remarks>
/// <para>
/// Gom vào một chỗ vì cả hai đều có phần dễ quên. Cất file mà quên ghi
/// <see cref="MediaAsset"/> thì file thành rác không ai biết đường xoá; ghi
/// <see cref="ProviderCall"/> mà quên cộng vào <see cref="AdVideoJob.ActualCostUsd"/> thì trần
/// chi tiêu của job không bao giờ chạm được.
/// </para>
/// <para>
/// <b>Mỗi lần gọi là một <c>SaveChanges</c> riêng.</b> Một job chạy hàng phút và có thể chết
/// giữa chừng; gom hết vào một transaction cuối nghĩa là mọi bằng chứng về tiền đã tiêu biến mất
/// cùng lúc với job. Bảng <see cref="ProviderCall"/> là sổ kế toán — nó phải được ghi ngay khi
/// tiền đã tiêu, kể cả khi job sắp fail.
/// </para>
/// </remarks>
public sealed class JobArtifacts
{
    private readonly AdVideoDbContext _db;
    private readonly IStorageService _storage;
    private readonly ILogger<JobArtifacts> _logger;

    public JobArtifacts(AdVideoDbContext db, IStorageService storage, ILogger<JobArtifacts> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Đưa nội dung lên storage và ghi một dòng <see cref="MediaAsset"/>.</summary>
    public async Task<MediaAsset> SaveAsync(
        PipelineContext context,
        AssetKind kind,
        string filename,
        Stream content,
        string contentType,
        CancellationToken cancellationToken,
        double? durationSeconds = null,
        bool hasAudio = false,
        int? width = null,
        int? height = null)
    {
        string bucket = Buckets.ForKind(kind);
        string objectKey = IStorageService.BuildKey(context.TenantId, context.JobId, kind, filename);

        StorageUploadResult upload = await _storage.UploadAsync(bucket, objectKey, content, contentType, cancellationToken);

        var asset = new MediaAsset
        {
            TenantId = context.TenantId,
            JobId = context.JobId,
            Kind = kind,
            Bucket = upload.Bucket,
            ObjectKey = upload.ObjectKey,
            ContentType = contentType,
            SizeBytes = upload.SizeBytes,
            DurationSeconds = durationSeconds,
            HasAudio = hasAudio,
            Width = width,
            Height = height,
        };

        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "job_id={JobId} lưu {Kind} {Bucket}/{ObjectKey} ({Bytes} bytes).",
            context.JobId,
            kind,
            asset.Bucket,
            asset.ObjectKey,
            asset.SizeBytes);

        return asset;
    }

    /// <summary>Tải file từ storage xuống một đường dẫn cục bộ để FFmpeg đọc được.</summary>
    public async Task DownloadToFileAsync(
        string bucket, string objectKey, string destinationPath, CancellationToken cancellationToken)
    {
        await using Stream source = await _storage.OpenReadAsync(bucket, objectKey, cancellationToken);
        await using FileStream destination = File.Create(destinationPath);

        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Ghi một lời gọi ra ngoài và cộng chi phí vào job.
    /// </summary>
    /// <remarks>
    /// Cộng dồn vào <paramref name="context"/> chứ không đọc lại tổng từ DB: bước sau phải thấy
    /// ngay tiền vừa tiêu để dừng trước khi vượt trần, và một truy vấn SUM sau mỗi shot là một
    /// vòng round-trip cho một con số ta đã có trong tay.
    /// </remarks>
    public async Task RecordCallAsync(
        PipelineContext context,
        AdVideoJob job,
        string provider,
        string modelId,
        ProviderCategory category,
        bool isSuccess,
        decimal costUsd,
        long durationMs,
        CancellationToken cancellationToken,
        int? shotIndex = null,
        string? providerRequestId = null,
        VideoFailureKind failureKind = VideoFailureKind.None,
        string? rawError = null,
        bool costIsReported = false,
        int? billedCharacters = null,
        int attemptNumber = 1,
        string? descriptorSha256 = null)
    {
        var call = new ProviderCall
        {
            JobId = context.JobId,
            TenantId = context.TenantId,
            ShotIndex = shotIndex,
            Provider = provider,
            ModelId = modelId,
            Category = category,
            IsSuccess = isSuccess,
            CostUsd = costUsd,
            CostIsReported = costIsReported,
            DurationMs = durationMs,
            ProviderRequestId = providerRequestId,
            FailureKind = failureKind,

            // Lỗi nguyên văn bị cắt: một provider trả cả trang HTML lỗi sẽ làm dòng log và cột
            // này phình ra mà 4000 ký tự đầu đã đủ để biết chuyện gì xảy ra. Che theo hình dạng
            // key là lưới an toàn thứ hai — adapter đã che đúng key của nó trước khi trả về.
            RawError = Truncate(SecretRedactor.RedactPatterns(rawError), 4000),
            BilledCharacterCount = billedCharacters,
            AttemptNumber = attemptNumber,
            DescriptorSha256 = descriptorSha256,
        };

        _db.ProviderCalls.Add(call);

        job.ActualCostUsd += costUsd;
        context.AccumulatedCostUsd += costUsd;

        await _db.SaveChangesAsync(cancellationToken);

        if (costUsd > 0)
        {
            _logger.LogInformation(
                "job_id={JobId} gọi {Provider}/{ModelId} lần {Attempt}: {Status}, {Cost} USD, {Duration} ms. Cộng dồn {Total} USD.",
                context.JobId,
                provider,
                modelId,
                attemptNumber,
                isSuccess ? "thành công" : failureKind.ToString(),
                costUsd,
                durationMs,
                context.AccumulatedCostUsd);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
