using System.Diagnostics;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Worker.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 6 — gọi provider sinh hình cho từng shot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Đây là bước tiêu tiền.</b> Mọi vòng lặp ở đây đều phải có trần: trần số lần thử lại
/// (<c>ShotMaxRetries</c>) và trần tiền của job. Một vòng retry không trần trên một provider
/// đang lỗi là cách tiêu hết ngân sách tháng trong một đêm.
/// </para>
/// <para>
/// <b>Một shot fail là cả job fail</b> (Luật 1). Không ghép video thiếu shot, cũng không đổi
/// provider giữa chừng: hai model cho ra hai tông hình khác nhau và chỗ nối sẽ lộ ngay.
/// </para>
/// <para>
/// <b>Tải clip về ngay.</b> URL provider trả về có thời hạn — Veo xoá file sau khoảng hai ngày.
/// Lưu URL vào DB thay vì tải file là một quả mìn hẹn giờ: job "thành công", khách tải được hôm
/// nay, và link chết vào thứ Năm tuần sau.
/// </para>
/// </remarks>
public sealed class RenderShotsStep : IPipelineStep
{
    /// <summary>Tên HttpClient dành riêng cho việc tải clip từ provider về.</summary>
    public const string HttpClientName = "advideo-provider-download";

    /// <summary>
    /// Hạn của URL ảnh tham chiếu gửi cho provider.
    /// </summary>
    /// <remarks>
    /// Dài hơn hẳn thời gian render vì provider có thể tải ảnh muộn — khi hàng đợi bên họ mới tới
    /// lượt job này. Không dùng <c>DownloadUrlLifetimeMinutes</c>: khoá đó là hạn link giao cho
    /// khách, và buộc hai thứ đi chung nghĩa là rút ngắn link của khách sẽ âm thầm làm hỏng việc
    /// dựng hình.
    /// </remarks>
    private static readonly TimeSpan ReferenceUrlLifetime = TimeSpan.FromHours(6);

    private readonly AdVideoDbContext _db;
    private readonly IProviderRegistry _registry;
    private readonly ISettingsStore _settings;
    private readonly IPromptStore _prompts;
    private readonly IStorageService _storage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JobArtifacts _artifacts;
    private readonly ILogger<RenderShotsStep> _logger;

    public RenderShotsStep(
        AdVideoDbContext db,
        IProviderRegistry registry,
        ISettingsStore settings,
        IPromptStore prompts,
        IStorageService storage,
        IHttpClientFactory httpClientFactory,
        JobArtifacts artifacts,
        ILogger<RenderShotsStep> logger)
    {
        _db = db;
        _registry = registry;
        _settings = settings;
        _prompts = prompts;
        _storage = storage;
        _httpClientFactory = httpClientFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public int Order => 6;

    public JobStatus RunningStatus => JobStatus.RenderingShots;

    public string DisplayName => "Đang dựng hình";

    public bool ShouldRun(PipelineContext context) => true;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        AdVideoJob? job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == context.JobId, cancellationToken);

        if (job is null)
        {
            return StepResult.Fail($"Không tìm thấy job {context.JobId} trong DB.");
        }

        if (context.ProviderName is null)
        {
            return StepResult.Fail("Job chưa chọn provider — bước khoá timeline phải chạy trước bước dựng hình.");
        }

        IVideoProvider? provider = await _registry.FindVideoProviderAsync(context.ProviderName, cancellationToken);

        if (provider is null)
        {
            // Không đổi sang provider khác (Luật 1): credential vừa bị tắt là chuyện của người vận
            // hành, còn một job nửa Veo nửa Kling thì lộ ngay ở chỗ chuyển cảnh.
            return StepResult.Fail(
                $"Provider \"{context.ProviderName}\" đã chọn cho job này không còn phục vụ. Hãy tạo job mới.");
        }

        List<Shot> shots = await _db.Shots
            .Where(s => s.JobId == context.JobId)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken);

        if (shots.Count == 0)
        {
            return StepResult.Fail("Không có shot nào để dựng — bước khoá timeline chưa ghi được gì.");
        }

        JobBrief brief = context.Brief();

        // Chốt cách xử lý tiếng gốc MỘT LẦN ở đây rồi dùng lại ở bước 8: tham số đã gửi cho
        // provider phải khớp với cách bước ghép xử lý track tiếng, nếu không thì video được trộn
        // theo một giả định mà provider không hề biết.
        NativeSoundDecision nativeSound = NativeSoundTranslator.Decide(
            provider.Capability,
            hasVoiceOver: context.VoiceAudioKey is not null,
            clientWantsSoundEffects: brief.WantsSoundEffects);

        context.SetNativeSound(nativeSound);

        _logger.LogInformation("job_id={JobId} tiếng gốc: {Reason}", context.JobId, nativeSound.Reason);

        string negativePrompt = await LoadNegativePromptAsync(cancellationToken);
        IReadOnlyList<string> referenceUrls = await BuildReferenceUrlsAsync(context, provider, cancellationToken);

        int maxRetries = await _settings.GetIntAsync(SettingKeys.ShotMaxRetries, 2, cancellationToken);
        int maxConcurrent = await _settings.GetIntAsync(SettingKeys.MaxConcurrentShots, 1, cancellationToken);

        if (maxConcurrent > 1)
        {
            // Nói thẳng thay vì im lặng bỏ qua: một setting được đặt mà không có tác dụng là thứ
            // người vận hành sẽ tin là đang chạy. Dựng song song cần mỗi shot một DbContext riêng
            // (DbContext không an toàn đa luồng) nên để dành cho sprint sau.
            _logger.LogWarning(
                "job_id={JobId} {Setting}={Value} nhưng sprint này dựng tuần tự; giá trị đó chưa có tác dụng.",
                context.JobId,
                SettingKeys.MaxConcurrentShots,
                maxConcurrent);
        }

        foreach (Shot shot in shots)
        {
            if (await TryResumeAsync(context, shot, cancellationToken))
            {
                continue;
            }

            StepResult result = await RenderShotAsync(
                context, job, shot, shots.Count, provider, negativePrompt, referenceUrls, nativeSound,
                maxRetries, cancellationToken);

            if (!result.IsSuccess)
            {
                return result;
            }
        }

        _logger.LogInformation(
            "job_id={JobId} dựng xong {Shots} shot bằng {Provider}, cộng dồn {Cost} USD.",
            context.JobId,
            shots.Count,
            provider.Name,
            context.AccumulatedCostUsd);

        return StepResult.Ok();
    }

    /// <summary>
    /// Shot này đã dựng xong ở lần chạy trước chưa.
    /// </summary>
    /// <remarks>
    /// Bước có thể chạy lại sau một <see cref="StepResult.Retry"/>, và dựng lại một shot đã có
    /// clip là trả tiền lần thứ hai cho đúng thứ đang nằm sẵn trong kho.
    /// </remarks>
    private async Task<bool> TryResumeAsync(PipelineContext context, Shot shot, CancellationToken cancellationToken)
    {
        if (shot.Status != ShotStatus.Rendered || shot.ClipAssetId is null)
        {
            return false;
        }

        MediaAsset? clip = await _db.MediaAssets
            .FirstOrDefaultAsync(a => a.Id == shot.ClipAssetId, cancellationToken);

        if (clip is null)
        {
            return false;
        }

        context.ShotClipKeys[shot.Index] = clip.ObjectKey;

        _logger.LogInformation(
            "job_id={JobId} shot {Shot} đã có clip từ lần chạy trước, bỏ qua.", context.JobId, shot.Index);

        return true;
    }

    private async Task<StepResult> RenderShotAsync(
        PipelineContext context,
        AdVideoJob job,
        Shot shot,
        int shotCount,
        IVideoProvider provider,
        string negativePrompt,
        IReadOnlyList<string> referenceUrls,
        NativeSoundDecision nativeSound,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        shot.Status = ShotStatus.Rendering;
        shot.RenderStartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            // Kiểm trần TRƯỚC mỗi lần gọi, kể cả lần thử lại: tiền tiêu ở shot trước có thể vừa
            // đẩy job chạm trần.
            if (context.IsCostCeilingHit)
            {
                shot.Status = ShotStatus.Failed;
                shot.LastError = "Chạm trần chi tiêu của job.";
                await _db.SaveChangesAsync(cancellationToken);

                return StepResult.Fail(
                    $"Đã tiêu {context.AccumulatedCostUsd:0.####} USD, chạm trần {context.MaxCostUsd:0.####} USD ở shot " +
                    $"{shot.Index + 1}/{shotCount}. Dừng trước khi tiêu thêm.");
            }

            var request = new VideoRequest
            {
                JobId = context.JobId,
                ShotIndex = shot.Index,
                Prompt = shot.VisualPrompt,
                DurationSeconds = shot.VideoDurationSeconds,
                AspectRatio = context.AspectRatio,
                ReferenceImageUrls = referenceUrls,
                NegativePrompt = negativePrompt,

                // Đổi seed mỗi lần thử: gửi lại y hệt cho một model tôn trọng seed thì nhận lại y
                // hệt kết quả hỏng — tức là trả tiền hai lần cho cùng một clip.
                Seed = provider.Capability.SupportsSeed ? DeriveSeed(context.JobId, shot.Index, attempt) : null,
                SuppressNativeAudio = nativeSound.ShouldRequestSuppression,
            };

            var stopwatch = Stopwatch.StartNew();
            VideoResult result = await provider.GenerateAsync(request, cancellationToken);
            stopwatch.Stop();

            // Provider nào không báo giá (fal.ai) thì suy từ manifest. Con số suy ra vẫn phải vào
            // sổ: một lần gọi không có dòng chi phí là một lần gọi mà trần chi tiêu không thấy.
            decimal cost = result.ReportedCostUsd
                ?? provider.Capability.CostPerSecondUsd * shot.VideoDurationSeconds;

            await _artifacts.RecordCallAsync(
                context,
                job,
                provider.Name,
                provider.Capability.ModelId,
                ProviderCategory.Video,
                result.IsSuccess,
                cost,
                stopwatch.ElapsedMilliseconds,
                cancellationToken,
                shotIndex: shot.Index,
                providerRequestId: result.ProviderRequestId,
                failureKind: result.FailureKind,
                rawError: result.RawError,
                costIsReported: result.ReportedCostUsd is not null,
                attemptNumber: attempt);

            shot.CostUsd += cost;
            shot.Seed = request.Seed;
            shot.ProviderModelId = provider.Capability.ModelId;
            shot.ProviderRequestId = result.ProviderRequestId;

            if (result.IsSuccess)
            {
                return await StoreClipAsync(context, shot, shotCount, result, cancellationToken);
            }

            shot.RetryCount = attempt - 1;
            shot.LastError = result.FailureReason;
            await _db.SaveChangesAsync(cancellationToken);

            string reason = result.FailureReason ?? "Provider không nói lý do.";

            if (!result.CanRetry)
            {
                // Nội dung bị từ chối: thử lại là trả tiền để nghe đúng câu trả lời đó lần nữa.
                shot.Status = ShotStatus.Failed;
                await _db.SaveChangesAsync(cancellationToken);

                return StepResult.Fail($"Shot {shot.Index + 1}/{shotCount}: {reason}", result.RawError);
            }

            if (attempt > maxRetries)
            {
                shot.Status = ShotStatus.Failed;
                await _db.SaveChangesAsync(cancellationToken);

                return StepResult.Fail(
                    $"Shot {shot.Index + 1}/{shotCount} thất bại sau {attempt} lần thử: {reason}", result.RawError);
            }

            // Tôn trọng Retry-After của provider. Bỏ qua nó khi đang bị giới hạn tần suất là cách
            // biến một lần chờ thành một chuỗi 429 dài hơn.
            int delaySeconds = result.RetryAfterSeconds ?? Math.Min(30, attempt * 5);

            _logger.LogWarning(
                "job_id={JobId} shot {Shot} lỗi {Kind} ở lần thử {Attempt}: {Reason}. Chờ {Delay}s rồi thử lại.",
                context.JobId,
                shot.Index,
                result.FailureKind,
                attempt,
                reason,
                delaySeconds);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        return StepResult.Fail($"Shot {shot.Index + 1}/{shotCount} không dựng được.");
    }

    private async Task<StepResult> StoreClipAsync(
        PipelineContext context,
        Shot shot,
        int shotCount,
        VideoResult result,
        CancellationToken cancellationToken)
    {
        byte[] bytes;

        if (result.VideoBytes is { Length: > 0 })
        {
            bytes = result.VideoBytes;
        }
        else if (result.VideoUri is not null)
        {
            try
            {
                HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

                bytes = await http.GetByteArrayAsync(result.VideoUri, cancellationToken);
            }
            catch (ProviderHostBlockedException ex)
            {
                // Không retry: lần sau vẫn bị chặn, và mỗi lần retry là một lần render trả tiền.
                return StepResult.Fail(
                    $"Shot {shot.Index + 1}/{shotCount}: provider trả link clip ở host không được phép. {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                // Clip này ĐÃ TRẢ TIỀN rồi. Retry được vì lỗi nằm ở đường truyền chứ không ở nội
                // dung — shot vẫn chưa Rendered nên lần chạy lại sẽ gọi provider lần nữa.
                return StepResult.Retry(
                    $"Shot {shot.Index + 1}/{shotCount} dựng xong nhưng tải về thất bại: {ex.Message}", ex.ToString());
            }
        }
        else
        {
            return StepResult.Fail(
                $"Shot {shot.Index + 1}/{shotCount}: provider báo thành công nhưng không trả về video nào.");
        }

        using var content = new MemoryStream(bytes, writable: false);

        MediaAsset asset = await _artifacts.SaveAsync(
            context,
            AssetKind.ShotClip,
            $"shot-{shot.Index:00}.mp4",
            content,
            "video/mp4",
            cancellationToken,
            durationSeconds: result.MeasuredDurationSeconds > 0
                ? result.MeasuredDurationSeconds
                : shot.VideoDurationSeconds,

            // Ghi lại track tiếng THẬT SỰ có trong file, không phải tham số đã gửi đi: bước 8 dựa
            // vào cờ này để biết phải strip hay có tiếng động để giữ.
            hasAudio: result.HasNativeAudio);

        shot.ClipAssetId = asset.Id;
        shot.Status = ShotStatus.Rendered;
        shot.RenderFinishedAt = DateTime.UtcNow;
        shot.LastError = null;

        await _db.SaveChangesAsync(cancellationToken);

        context.ShotClipKeys[shot.Index] = asset.ObjectKey;

        return StepResult.Ok();
    }

    /// <summary>Negative prompt toàn cục, lấy từ DB (D7/D10) chứ không hard-code trong adapter.</summary>
    private async Task<string> LoadNegativePromptAsync(CancellationToken cancellationToken)
    {
        string code = await _settings.GetStringAsync(SettingKeys.GlobalNegativePromptCode, cancellationToken)
            ?? PromptTemplateSeeder.GlobalNegativeCode;

        return await _prompts.GetActiveContentAsync(
            code, PromptTemplateSeeder.GlobalNegativeContent, cancellationToken);
    }

    /// <summary>
    /// URL ảnh tham chiếu gửi cho provider.
    /// </summary>
    /// <remarks>
    /// Presigned chứ không phải link công khai: ảnh sản phẩm của khách không được nằm sau một URL
    /// đoán được. Cắt theo <c>MaxReferenceImages</c> ở đây thay vì để adapter tự lo, vì gửi thừa
    /// ảnh thì provider trả lỗi 400 sau khi đã mất một vòng gọi.
    /// </remarks>
    private async Task<IReadOnlyList<string>> BuildReferenceUrlsAsync(
        PipelineContext context,
        IVideoProvider provider,
        CancellationToken cancellationToken)
    {
        int max = Math.Max(1, provider.Capability.MaxReferenceImages);
        var urls = new List<string>(Math.Min(max, context.ProductImageKeys.Count));

        foreach (string key in context.ProductImageKeys.Take(max))
        {
            Uri url = await _storage.GetPresignedUrlAsync(
                Buckets.Uploads, key, ReferenceUrlLifetime, cancellationToken);

            urls.Add(url.ToString());
        }

        if (context.ProductImageKeys.Count > max)
        {
            _logger.LogInformation(
                "job_id={JobId} có {Have} ảnh nhưng {Provider} chỉ nhận {Max}; bỏ {Dropped} ảnh cuối.",
                context.JobId,
                context.ProductImageKeys.Count,
                provider.Name,
                max,
                context.ProductImageKeys.Count - max);
        }

        return urls;
    }

    /// <summary>
    /// Seed suy ra từ (job, shot, lần thử) thay vì random.
    /// </summary>
    /// <remarks>
    /// Chạy lại cùng một job cho ra cùng một seed, nghĩa là tái hiện được kết quả để đối chiếu khi
    /// khách khiếu nại. Random thì mỗi lần một kiểu và không so sánh được gì.
    /// </remarks>
    private static int DeriveSeed(Guid jobId, int shotIndex, int attempt)
    {
        int hash = HashCode.Combine(jobId, shotIndex, attempt);

        return hash == int.MinValue ? 1 : Math.Abs(hash);
    }
}
