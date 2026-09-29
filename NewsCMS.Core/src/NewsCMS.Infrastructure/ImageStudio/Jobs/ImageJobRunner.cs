using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.ImageStudio.Jobs;

public interface IImageJobRunner
{
    /// <summary>Nhận job (nếu còn đang chờ), gọi provider, lưu ảnh. Không ném lỗi — lỗi ghi vào job.</summary>
    Task RunAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Chạy một job tạo ảnh. Mỗi biến thể là một lần gọi provider riêng, chạy song song; mỗi lần gọi có
/// một dòng <see cref="ImageProviderCall"/>, kể cả lần lỗi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thử lại đúng một lần</b>, và chỉ khi lỗi cho thấy provider chưa làm gì (429, 502/503/504).
/// Lỗi nội dung, sai key, sai tham số thì dừng: thử lại chỉ tốn thêm tiền mà kết quả vẫn vậy.
/// </para>
/// <para>
/// Chạy ngoài HTTP request nên global filter site khớp 0 dòng: đọc job bằng
/// <c>IgnoreQueryFilters</c> và dùng <c>SiteId</c> của chính job.
/// </para>
/// </remarks>
public sealed class ImageJobRunner : IImageJobRunner
{
    public const string OutputFolder = "ai-images/outputs";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly AppDbContext _db;
    private readonly ImageModelCredentials _credentials;
    private readonly IImageProviderRegistry _registry;
    private readonly IFileStorage _storage;
    private readonly ILogger<ImageJobRunner> _logger;

    public ImageJobRunner(
        AppDbContext db,
        ImageModelCredentials credentials,
        IImageProviderRegistry registry,
        IFileStorage storage,
        ILogger<ImageJobRunner> logger)
    {
        _db = db;
        _credentials = credentials;
        _registry = registry;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Thời gian chờ giữa hai lần thử — test đặt 0.</summary>
    public TimeSpan RetryWait { get; set; } = RetryDelay;

    public async Task RunAsync(Guid jobId, CancellationToken ct = default)
    {
        ImageJob? job = await _db.ImageJobs.IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null || job.Status != ImageJobStatus.Queued)
        {
            return;
        }

        job.Status = ImageJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Instance khác đã nhận, hoặc người dùng vừa huỷ.
            return;
        }

        try
        {
            await ExecuteAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = ImageJobStatus.Failed;
            job.Error = "Máy chủ dừng giữa chừng. Hãy tạo lại.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job tạo ảnh {JobId} lỗi.", job.Id);
            job.Status = ImageJobStatus.Failed;
            job.Error = "Lỗi hệ thống khi tạo ảnh. Hãy thử lại.";
        }

        job.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ExecuteAsync(ImageJob job, CancellationToken ct)
    {
        Result<(ImageModel Model, ImageProviderContext Context)> resolved = await _credentials.ResolveAsync(job.ImageModelId, requireActive: false, ct);

        if (!resolved.Succeeded)
        {
            job.Status = ImageJobStatus.Failed;
            job.Error = resolved.Error;
            return;
        }

        (ImageModel model, ImageProviderContext context) = resolved.Value;
        IImageProvider? provider = _registry.Get(model.Adapter);

        if (provider is null)
        {
            job.Status = ImageJobStatus.Failed;
            job.Error = $"Loại kết nối của model \"{model.Name}\" chưa được hỗ trợ trên máy chủ này.";
            return;
        }

        var request = new ImageGenerateRequest(context, job.FinalPrompt, job.Size);

        // Gọi mạng song song; DbContext không an toàn đa luồng nên mọi thứ ghi DB làm tuần tự bên dưới.
        List<ImageProviderResult>[] attempts = await Task.WhenAll(
            Enumerable.Range(0, job.VariantCount).Select(_ => CallWithRetryAsync(provider, request, ct)));

        int index = 0;
        int saved = 0;
        decimal cost = 0;
        string? firstError = null;

        foreach (List<ImageProviderResult> variant in attempts)
        {
            foreach (ImageProviderResult attempt in variant)
            {
                ImageProviderCall call = NewCall(job, model, attempt);
                _db.ImageProviderCalls.Add(call);

                if (!attempt.Ok)
                {
                    firstError ??= attempt.ErrorMessage;
                    continue;
                }

                cost += call.CostUsd;

                try
                {
                    FinalizedImage image = AiImageFinalizer.Finalize(attempt.Image!.Bytes, model.OutputFormat, AiImageFinalizer.TrainedAlgorithmicMedia);
                    ImageJobOutput output = await SaveOutputAsync(job, image, index++, ct);

                    // Add qua DbSet, không qua job.Outputs: Id Guid đã có sẵn nên thêm vào navigation thì
                    // EF tưởng là dòng cũ và sinh UPDATE thay vì INSERT. EF tự gắn vào job.Outputs.
                    _db.ImageJobOutputs.Add(output);
                    saved++;
                }
                catch (InvalidDataException ex)
                {
                    // Provider tính tiền nhưng trả rác: vẫn ghi chi phí, đánh dấu lời gọi là hỏng.
                    call.ErrorCode = ImageProviderErrorCodes.InvalidResponse;
                    call.ErrorMessage = ex.Message;
                    firstError ??= ex.Message;
                }
            }
        }

        job.CostUsd = cost;

        if (saved == 0)
        {
            job.Status = ImageJobStatus.Failed;
            job.Error = firstError ?? "Không tạo được ảnh nào.";
        }
        else
        {
            job.Status = ImageJobStatus.Succeeded;
            job.Error = saved < job.VariantCount
                ? $"Tạo được {saved}/{job.VariantCount} ảnh. Lỗi còn lại: {firstError}"
                : null;
        }
    }

    private async Task<List<ImageProviderResult>> CallWithRetryAsync(IImageProvider provider, ImageGenerateRequest request, CancellationToken ct)
    {
        ImageProviderResult first = await provider.GenerateAsync(request, ct);

        if (first.Ok || !IsRetryable(first))
        {
            return [first];
        }

        await Task.Delay(RetryWait, ct);

        return [first, await provider.GenerateAsync(request, ct)];
    }

    /// <summary>Chỉ những lỗi cho thấy provider chưa sinh ảnh (và chưa tính tiền).</summary>
    public static bool IsRetryable(ImageProviderResult result) =>
        result.ErrorCode == ImageProviderErrorCodes.RateLimited
        || (result.ErrorCode == ImageProviderErrorCodes.ProviderError && result.HttpStatus is null or 502 or 503 or 504);

    private async Task<ImageJobOutput> SaveOutputAsync(ImageJob job, FinalizedImage image, int index, CancellationToken ct)
    {
        await using var stream = new MemoryStream(image.Bytes);
        string key = await _storage.SaveAsync(stream, $"ai-image.{image.Extension}", OutputFolder, ct);

        return new ImageJobOutput
        {
            JobId = job.Id,
            Index = index,
            StorageKey = key,
            Url = _storage.GetPublicUrl(key),
            Width = image.Width,
            Height = image.Height,
            Bytes = image.Bytes.LongLength,
            MimeType = image.MimeType,
        };
    }

    internal static ImageProviderCall NewCall(ImageJob? job, ImageModel model, ImageProviderResult result, string operation = "generate", Guid? siteId = null) => new()
    {
        JobId = job?.Id,
        SiteId = job?.SiteId ?? siteId ?? Guid.Empty,
        ImageModelId = model.Id,
        ModelId = model.ModelId,
        Operation = operation,
        HttpStatus = result.HttpStatus,
        DurationMs = result.DurationMs,
        ImagesReturned = result.Ok ? 1 : 0,
        CostUsd = result.Ok ? model.PricePerImageUsd : 0,
        ErrorCode = result.ErrorCode,
        ErrorMessage = result.ErrorMessage is { Length: > 1000 } m ? m[..1000] : result.ErrorMessage,
    };
}
