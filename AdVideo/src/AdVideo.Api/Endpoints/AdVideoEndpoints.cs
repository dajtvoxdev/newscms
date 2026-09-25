using System.Text.Json;
using AdVideo.Api.Auth;
using AdVideo.Api.Contracts;
using AdVideo.Core.Configuration;
using AdVideo.Core.Costing;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Idempotency;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Endpoints;

/// <summary>
/// Endpoint tạo và tra cứu job.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thứ tự kiểm tra trong <c>POST</c> không tuỳ tiện</b>: chống trùng → kiểm dữ liệu → chọn
/// provider → dự toán tiền → trần chi tiêu → mới ghi DB. Mọi thứ tốn tiền hoặc tốn bộ nhớ đều
/// nằm sau mọi thứ rẻ. Đổi thứ tự là mở đường cho một request rác kéo cả hệ thống đi làm việc
/// trước khi bị từ chối.
/// </para>
/// </remarks>
public static class AdVideoEndpoints
{
    /// <summary>Header chống trùng. Bắt buộc trên mọi request tạo job.</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// Ước lượng thô thời gian chờ một shot, để trả <c>estimated_ready_at</c>.
    /// </summary>
    /// <remarks>
    /// Con số <b>phỏng đoán</b> — Sprint 0 (đo thật) bị hoãn. Nó chỉ dùng để hiển thị, không dùng
    /// cho timeout hay cho quyết định nào. Khi có số đo thật thì chuyển hẳn thành một
    /// <c>SystemSetting</c> chứ đừng sửa hằng số này.
    /// </remarks>
    private const int ProvisionalSecondsPerShot = 90;

    public static IEndpointRouteBuilder MapAdVideoEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/ad-videos").RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdVideo")
            .WithSummary("Tạo một job dựng video quảng cáo.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAdVideo")
            .WithSummary("Trạng thái, tiến độ và link tải của một job.");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http,
        [FromBody] CreateAdVideoRequest? body,
        AdVideoDbContext db,
        IProviderRegistry registry,
        ISettingsStore settings,
        IBackgroundJobClient backgroundJobs,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(AdVideoEndpoints));
        Guid tenantId = http.User.GetTenantId();

        string? idempotencyKey = http.Request.Headers[IdempotencyHeader].ToString();
        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        // 1. Chống trùng phải chạy TRƯỚC khi kiểm dữ liệu: một client retry vì timeout đang gửi
        //    lại đúng request cũ, và nó cần nhận lại job cũ chứ không phải một bản kiểm dữ liệu mới.
        string requestHash = ComputeRequestHash(body);

        AdVideoJob? existing = idempotencyKey is null
            ? null
            : await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken);

        IdempotencyDecision decision = IdempotencyGuard.Decide(
            idempotencyKey, existing?.Id, existing?.RequestHash, requestHash);

        switch (decision.Outcome)
        {
            case IdempotencyOutcome.RejectMissingKey:
                return Problem(http, StatusCodes.Status400BadRequest, "Thiếu Idempotency-Key", decision.Reason);

            case IdempotencyOutcome.RejectConflict:
                return Problem(http, StatusCodes.Status409Conflict, "Idempotency-Key đã dùng cho request khác", decision.Reason);

            case IdempotencyOutcome.ReturnExisting:
                logger.LogInformation(
                    "Trả lại job {JobId} cho Idempotency-Key {Key} — không tạo job mới.",
                    existing!.Id,
                    idempotencyKey);

                http.Response.Headers.Location = $"/v1/ad-videos/{existing.Id}";

                return Results.Ok(AdVideoJobResponse.From(existing));
        }

        // 2. Kiểm dữ liệu.
        (ValidatedRequest? request, IDictionary<string, string[]> errors) =
            CreateAdVideoRequestValidator.Validate(body);

        if (request is null)
        {
            return Results.ValidationProblem(
                errors,
                title: "Request không hợp lệ",
                detail: "Sửa các trường bên dưới rồi gửi lại. Có thể dùng lại Idempotency-Key cũ vì chưa job nào được tạo.");
        }

        // 3. Chọn provider. Khách không chọn provider (Luật 3) — trừ khi cố ý ép để chẩn đoán.
        IVideoProvider? videoProvider;

        if (request.ForcedProvider is { } forced)
        {
            videoProvider = await registry.FindVideoProviderAsync(forced, cancellationToken);

            if (videoProvider is null)
            {
                return Problem(
                    http,
                    StatusCodes.Status422UnprocessableEntity,
                    "Provider không dùng được",
                    $"Provider \"{forced}\" không tồn tại hoặc đang tắt. Bỏ trống options.provider để hệ thống tự chọn.");
            }
        }
        else
        {
            ProviderSelectionResult selection = await registry.SelectVideoProviderAsync(
                new VideoRequirements
                {
                    Tier = request.Tier,
                    HasPerson = request.HasPerson,
                    TargetDurationSeconds = request.DurationSeconds,
                    AspectRatio = request.AspectRatio,
                },
                cancellationToken);

            if (!selection.IsSuccess)
            {
                return Problem(
                    http,
                    StatusCodes.Status422UnprocessableEntity,
                    "Không có provider nào đáp ứng được yêu cầu này",
                    string.Join(" ", selection.Reasons),
                    extensions: new Dictionary<string, object?>
                    {
                        // 422 không kèm gợi ý là 422 vô dụng: khách biết mình sai mà không biết sửa thế nào.
                        ["suggestions"] = selection.Suggestions.Select(s => new
                        {
                            provider = s.ProviderName,
                            quality = s.Tier.ToString().ToLowerInvariant(),
                            reason = s.Reason,
                            estimated_cost_usd = s.EstimatedCostUsd,
                        }),
                    });
            }

            videoProvider = await registry.FindVideoProviderAsync(selection.ProviderName!, cancellationToken);

            if (videoProvider is null)
            {
                // Chọn được tên nhưng dựng không được: credential vừa bị tắt giữa hai lời gọi.
                return Problem(
                    http,
                    StatusCodes.Status503ServiceUnavailable,
                    "Provider vừa ngừng phục vụ",
                    $"Provider \"{selection.ProviderName}\" được chọn nhưng không khởi tạo được. Thử lại sau ít phút.");
            }
        }

        ProviderSelectionResult ttsSelection = await registry.SelectTtsProviderAsync(request.Tier, cancellationToken);

        if (!ttsSelection.IsSuccess)
        {
            return Problem(
                http,
                StatusCodes.Status422UnprocessableEntity,
                "Không có engine giọng đọc nào đáp ứng được",
                string.Join(" ", ttsSelection.Reasons));
        }

        ITtsProvider? ttsProvider = await registry.FindTtsProviderAsync(ttsSelection.ProviderName!, cancellationToken);

        // 4. Dự toán tiền, rồi mới tới hai cái trần.
        int maxRetries = await settings.GetIntAsync(SettingKeys.ShotMaxRetries, 2, cancellationToken);

        CostEstimate estimate = CostEstimator.Estimate(
            request.DurationSeconds,
            videoProvider.Capability,
            ttsProvider?.Capability,
            maxRetries,
            request.Script);

        decimal systemMaxPerJob = await settings.GetDecimalAsync(SettingKeys.MaxCostPerJobUsd, 2m, cancellationToken);

        // Khách hạ trần xuống được, nâng lên thì không: trần hệ thống là trần thật.
        decimal effectiveMax = request.MaxCostUsd is { } clientMax
            ? Math.Min(clientMax, systemMaxPerJob)
            : systemMaxPerJob;

        if (estimate.WorstCaseCostUsd > effectiveMax)
        {
            return Problem(
                http,
                StatusCodes.Status422UnprocessableEntity,
                "Vượt trần chi tiêu",
                $"Job này tốn tối đa {estimate.WorstCaseCostUsd:0.####} USD (đã tính {maxRetries} lần render lại), " +
                $"vượt trần {effectiveMax:0.####} USD. Rút ngắn thời lượng, hạ chất lượng, hoặc nâng trần.",
                extensions: new Dictionary<string, object?>
                {
                    ["estimated_cost_usd"] = estimate.EstimatedCostUsd,
                    ["worst_case_cost_usd"] = estimate.WorstCaseCostUsd,
                    ["limit_usd"] = effectiveMax,
                    ["shot_count"] = estimate.ShotCount,
                });
        }

        decimal dailyLimit = await settings.GetDecimalAsync(SettingKeys.DailySystemCostLimitUsd, 20m, cancellationToken);
        decimal spentToday = await SpentTodayAsync(db, cancellationToken);

        if (spentToday + estimate.EstimatedCostUsd > dailyLimit)
        {
            logger.LogWarning(
                "Chạm trần chi tiêu ngày: đã tiêu {Spent} USD, trần {Limit} USD. Từ chối job mới.",
                spentToday,
                dailyLimit);

            http.Response.Headers.RetryAfter = "3600";

            return Problem(
                http,
                StatusCodes.Status503ServiceUnavailable,
                "Hệ thống đã chạm trần chi tiêu trong ngày",
                $"Đã tiêu {spentToday:0.##}/{dailyLimit:0.##} USD hôm nay. Thử lại sau, hoặc liên hệ bên vận hành để nâng trần.");
        }

        // 5. Ghi DB.
        var job = new AdVideoJob
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey!,
            RequestHash = requestHash,
            Status = JobStatus.Queued,
            CurrentStep = 0,
            Tier = request.Tier,
            AspectRatio = request.AspectRatio,
            TargetDurationSeconds = request.DurationSeconds,
            BriefJson = SerializeBrief(body),
            Provider = videoProvider.Name,
            ProviderModelId = videoProvider.Capability.ModelId,
            HasPerson = request.HasPerson,
            EstimatedCostUsd = estimate.EstimatedCostUsd,
            MaxCostUsd = effectiveMax,
            RequiresApproval = false,
            QueuedAt = DateTime.UtcNow,
        };

        db.Jobs.Add(job);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Hai request cùng key tới cùng lúc: unique index là trọng tài, không phải đoạn code
            // kiểm tra ở trên. Kẻ thua cuộc trả về job của kẻ thắng.
            db.ChangeTracker.Clear();

            AdVideoJob? winner = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken);

            if (winner is null)
            {
                throw;
            }

            logger.LogInformation("Hai request cùng Idempotency-Key {Key}; trả job {JobId}.", idempotencyKey, winner.Id);

            return Results.Ok(AdVideoJobResponse.From(winner));
        }

        // 6. Đẩy vào hàng đợi SAU khi đã commit: enqueue trước thì worker có thể cầm job trước khi
        //    transaction xong và thấy một job không tồn tại.
        backgroundJobs.Enqueue<IAdVideoJobRunner>(runner => runner.RunAsync(job.Id, CancellationToken.None));

        logger.LogInformation(
            "Tạo job {JobId} cho tenant {TenantId}: {Shots} shot, provider {Provider}, dự toán {Cost} USD.",
            job.Id,
            tenantId,
            estimate.ShotCount,
            videoProvider.Name,
            estimate.EstimatedCostUsd);

        DateTime readyAt = DateTime.UtcNow.AddSeconds(estimate.ShotCount * ProvisionalSecondsPerShot);

        http.Response.Headers.Location = $"/v1/ad-videos/{job.Id}";

        return Results.Accepted($"/v1/ad-videos/{job.Id}", AdVideoJobResponse.From(job, estimatedReadyAt: readyAt));
    }

    private static async Task<Results<Ok<AdVideoJobResponse>, NotFound<ProblemDetails>>> GetAsync(
        Guid id,
        AdVideoDbContext db,
        IStorageService storage,
        ISettingsStore settings,
        CancellationToken cancellationToken)
    {
        // Global query filter đã ghim tenant; không cần (và không được) lọc tay thêm ở đây, vì
        // lọc tay đúng một chỗ rồi quên ở chỗ khác chính là cách dữ liệu rò sang tenant khác.
        AdVideoJob? job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy job",
                Detail = $"Không có job {id} thuộc về tenant này.",
            });
        }

        string? downloadUrl = null;

        if (job is { Status: JobStatus.Completed, FinalVideoAssetId: { } assetId })
        {
            MediaAsset? asset = await db.MediaAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

            if (asset is not null)
            {
                int lifetime = await settings.GetIntAsync(
                    SettingKeys.DownloadUrlLifetimeMinutes, 60, cancellationToken);

                Uri presigned = await storage.GetPresignedUrlAsync(
                    asset.Bucket, asset.ObjectKey, TimeSpan.FromMinutes(lifetime), cancellationToken);

                downloadUrl = presigned.ToString();
            }
        }

        return TypedResults.Ok(AdVideoJobResponse.From(job, downloadUrl));
    }

    /// <summary>Tổng tiền đã tiêu hôm nay, tính trên TOÀN hệ thống chứ không theo tenant.</summary>
    /// <remarks>
    /// <c>IgnoreQueryFilters</c> vì đây là câu hỏi của bên vận hành ("hôm nay hệ thống tiêu bao
    /// nhiêu"), không phải của khách. Không bỏ filter thì mỗi tenant chỉ thấy phần của mình và
    /// trần toàn hệ thống trở thành trần theo tenant — nghĩa là hệ thống có thể tiêu gấp N lần
    /// trần mà không có gì báo.
    /// </remarks>
    private static async Task<decimal> SpentTodayAsync(AdVideoDbContext db, CancellationToken cancellationToken)
    {
        DateTime since = DateTime.UtcNow.Date;

        return await db.ProviderCalls
            .IgnoreQueryFilters()
            .Where(c => c.CreatedAt >= since)
            .SumAsync(c => c.CostUsd, cancellationToken);
    }

    /// <summary>
    /// Hash của request để so khi <c>Idempotency-Key</c> trùng.
    /// </summary>
    /// <remarks>
    /// Hash trên JSON đã chuẩn hoá (serialize lại từ object đã parse) chứ không trên byte thô:
    /// cùng một payload gửi lại với khoảng trắng khác hoặc thứ tự trường khác vẫn là cùng một
    /// request, và bắt khách gửi byte giống hệt là một yêu cầu không ai đáp ứng được.
    /// </remarks>
    private static string ComputeRequestHash(CreateAdVideoRequest? body)
    {
        string canonical = JsonSerializer.Serialize(body, ApiJson.Canonical);

        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    /// <summary>Brief gốc lưu nguyên văn để về sau chứng minh được khách đã gửi gì (R3).</summary>
    private static string SerializeBrief(CreateAdVideoRequest? body) =>
        JsonSerializer.Serialize(body, ApiJson.Canonical);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private static IResult Problem(
        HttpContext http,
        int status,
        string title,
        string? detail,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
            Type = $"https://advideo/errors/{status}",
        };

        if (extensions is not null)
        {
            foreach (KeyValuePair<string, object?> pair in extensions)
            {
                problem.Extensions[pair.Key] = pair.Value;
            }
        }

        return Results.Problem(problem);
    }
}
