using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.Ai.PromptLibrary;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Cập nhật kho mẫu theo xu hướng: gọi skill AI <c>videostudio_trend_templates</c> (kèm công cụ tìm
/// web nếu đã bật), kiểm tra từng mẫu, lưu mẫu đạt.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mẫu AI không được tin mặc định.</b> Mỗi mẫu qua đúng luật của mẫu viết tay
/// (<see cref="VideoPromptTemplateRules"/>): cụm quảng cáo tuyệt đối, ngành nhạy cảm, khung hình,
/// thời lượng. Thêm hai luật riêng: lời thoại phải có chỗ giữ <c>{san_pham}</c> (không thì mẫu đang
/// quảng cáo sản phẩm của ai đó khác), và không trùng tiêu đề mẫu đã có.
/// </para>
/// <para>
/// <b>Không bao giờ chạy chồng.</b> Khoá trong tiến trình cho "Cập nhật ngay" bấm lúc lịch đang chạy;
/// thêm kiểm tra DB (một lần chạy dở dưới 30 phút) cho trường hợp nhiều instance.
/// </para>
/// </remarks>
public sealed class VideoPromptTrendService : IVideoPromptTrendService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan StaleRun = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IAiCompletionService _ai;
    private readonly ILogger<VideoPromptTrendService> _logger;

    public VideoPromptTrendService(AppDbContext db, IAiCompletionService ai, ILogger<VideoPromptTrendService> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    public async Task<bool> IsDueAsync(CancellationToken ct = default)
    {
        VideoPromptLibrarySettings? settings = await _db.VideoPromptLibrarySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);

        if (settings is not { TrendAutoUpdateEnabled: true })
        {
            return false;
        }

        DateTime? last = await _db.VideoPromptTrendRuns.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Select(x => (DateTime?)x.StartedAt)
            .FirstOrDefaultAsync(ct);

        return TrendRunSupport.IsDue(true, settings.TrendIntervalHours, last, DateTime.UtcNow);
    }

    public async Task<Result<VideoPromptTrendRunDto>> RefreshAsync(string trigger, CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct))
        {
            return Result<VideoPromptTrendRunDto>.Failure("Đang có một lần cập nhật chạy. Đợi xong rồi thử lại.");
        }

        try
        {
            DateTime now = DateTime.UtcNow;

            if (await _db.VideoPromptTrendRuns.AnyAsync(x => x.Status == VideoPromptTrendRunStatus.Running && x.StartedAt > now - StaleRun, ct))
            {
                return Result<VideoPromptTrendRunDto>.Failure("Đang có một lần cập nhật chạy. Đợi xong rồi thử lại.");
            }

            VideoPromptLibrarySettings settings = await _db.VideoPromptLibrarySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct)
                ?? new VideoPromptLibrarySettings();

            var run = new VideoPromptTrendRun
            {
                Trigger = trigger.Length > 200 ? trigger[..200] : trigger,
                Status = VideoPromptTrendRunStatus.Running,
                StartedAt = now,
                UsedWebSearch = await TrendRunSupport.HasWebSearchAsync(_db, AiTaskKeys.VideoStudioTrendTemplates, ct),
            };
            _db.VideoPromptTrendRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            try
            {
                await ExecuteAsync(run, settings, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cập nhật kho prompt theo trend lỗi (run {RunId}).", run.Id);
                run.Status = VideoPromptTrendRunStatus.Failed;
                run.Notes = TrendRunSupport.Append(run.Notes, $"Lỗi: {ex.Message}");
            }

            run.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            VideoPromptTrendRunDto dto = VideoPromptLibraryService.ToDto(run);

            return run.Status == VideoPromptTrendRunStatus.Succeeded
                ? Result<VideoPromptTrendRunDto>.Success(dto)
                : Result<VideoPromptTrendRunDto>.Failure(run.Notes ?? "Cập nhật thất bại.");
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task ExecuteAsync(VideoPromptTrendRun run, VideoPromptLibrarySettings settings, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        // 1. Mẫu trend hết hạn: ẩn đi (vẫn giữ trong kho để quản trị xem lại).
        List<VideoPromptTemplate> expired = await _db.VideoPromptTemplates
            .Where(x => x.Source == VideoPromptTemplateSource.Trend
                        && x.Status != VideoPromptTemplateStatus.Hidden
                        && x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (VideoPromptTemplate template in expired)
        {
            template.Status = VideoPromptTemplateStatus.Hidden;
        }

        run.Expired = expired.Count;
        await _db.SaveChangesAsync(ct);

        // 2. Xin AI mẫu mới.
        List<string> existingTitles = await _db.VideoPromptTemplates.AsNoTracking()
            .Where(x => x.Status != VideoPromptTemplateStatus.Hidden)
            .Select(x => x.Title)
            .ToListAsync(ct);

        Result<AiGenerationResult> generated = await _ai.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.VideoStudioTrendTemplates,
            AiGenerationStyle.Plain,
            Title: null,
            Content: BuildRequest(settings, existingTitles, now),
            Selection: null,
            Language: "vi",
            ConnectionId: null), ct);

        if (!generated.Succeeded)
        {
            run.Status = VideoPromptTrendRunStatus.Failed;
            run.Notes = TrendRunSupport.Append(run.Notes,
                $"AI không trả lời: {generated.Error} Kiểm tra kết nối AI mặc định và skill \"{AiTaskKeys.VideoStudioTrendTemplates}\" ở trang AI.");
            return;
        }

        List<TrendItem>? items = TrendRunSupport.ParseArray<TrendItem>(generated.Value!.Content, out string? parseError);

        if (items is null)
        {
            run.Status = VideoPromptTrendRunStatus.Failed;
            run.Notes = TrendRunSupport.Append(run.Notes, $"Không đọc được kết quả của AI: {parseError}");
            return;
        }

        // 3. Kiểm tra từng mẫu, lưu mẫu đạt.
        HashSet<string> seen = existingTitles.Select(VideoPromptTemplateRules.NormalizeTitle).ToHashSet(StringComparer.Ordinal);
        int limit = Math.Clamp(settings.TemplatesPerRun, 1, 20);

        foreach (TrendItem item in items)
        {
            if (run.Added >= limit)
            {
                break;
            }

            string title = item.Title?.Trim() ?? "";
            var input = new VideoPromptTemplateInput(
                title,
                VideoPromptTemplateRules.Categories.Contains(item.Category?.Trim() ?? "") ? item.Category!.Trim() : "Khác",
                item.Description,
                item.ScenePrompt,
                item.Script,
                VideoPromptTemplateRules.AspectRatios.Contains(item.AspectRatio ?? "") ? item.AspectRatio : "9:16",
                item.DurationSeconds ?? 15,
                item.HasPerson ?? false,
                0);

            List<string> errors = VideoPromptTemplateRules.Validate(input);

            if (!(item.Script ?? "").Contains(VideoPromptTemplate.ProductPlaceholder, StringComparison.Ordinal))
            {
                errors.Add($"Lời thoại không có chỗ giữ {VideoPromptTemplate.ProductPlaceholder}.");
            }

            if (title.Length > 0 && !seen.Add(VideoPromptTemplateRules.NormalizeTitle(title)))
            {
                errors.Add("Trùng tiêu đề mẫu đã có.");
            }

            if (errors.Count > 0)
            {
                run.Rejected++;
                run.Notes = TrendRunSupport.Append(run.Notes, $"Loại \"{TrendRunSupport.Truncate(title, 80)}\": {string.Join(" ", errors)}");
                continue;
            }

            _db.VideoPromptTemplates.Add(new VideoPromptTemplate
            {
                Title = title,
                Category = input.Category!,
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : TrendRunSupport.Truncate(input.Description.Trim(), 500),
                ScenePrompt = input.ScenePrompt!.Trim(),
                ScriptTemplate = input.ScriptTemplate!.Trim(),
                AspectRatio = input.AspectRatio!,
                DurationSeconds = input.DurationSeconds,
                HasPerson = input.HasPerson,
                Source = VideoPromptTemplateSource.Trend,
                Status = settings.RequireReview ? VideoPromptTemplateStatus.PendingReview : VideoPromptTemplateStatus.Published,
                TrendName = string.IsNullOrWhiteSpace(item.TrendName) ? null : TrendRunSupport.Truncate(item.TrendName.Trim(), 200),
                SourceUrls = TrendRunSupport.CleanUrls(item.SourceUrls),
                ExpiresAt = now.AddDays(Math.Clamp(settings.TrendLifetimeDays, 1, 180)),
                TrendRunId = run.Id,
            });
            run.Added++;
        }

        if (!run.UsedWebSearch)
        {
            run.Notes = TrendRunSupport.Append(run.Notes, "Chưa bật công cụ tìm web — xu hướng chỉ dựa trên hiểu biết của model, có thể không mới.");
        }

        run.Status = VideoPromptTrendRunStatus.Succeeded;
        await _db.SaveChangesAsync(ct);
    }

    private static string BuildRequest(VideoPromptLibrarySettings settings, IReadOnlyList<string> existingTitles, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hôm nay là {now:yyyy-MM-dd}.");
        sb.AppendLine($"Cần {Math.Clamp(settings.TemplatesPerRun, 1, 20)} mẫu mới, mỗi mẫu một xu hướng khác nhau, trải đều nhiều ngành.");

        if (!string.IsNullOrWhiteSpace(settings.Focus))
        {
            sb.AppendLine($"Trọng tâm: {settings.Focus}");
        }

        if (existingTitles.Count > 0)
        {
            sb.AppendLine("Kho đã có các mẫu sau — KHÔNG lặp lại ý tưởng hay tiêu đề:");

            foreach (string title in existingTitles.Take(80))
            {
                sb.AppendLine($"- {title}");
            }
        }

        sb.AppendLine("Trả về đúng một mảng JSON theo hợp đồng trong hướng dẫn hệ thống.");

        return sb.ToString();
    }

    /// <summary>Một mẫu AI trả về — tên trường snake_case theo hợp đồng trong prompt hệ thống của skill.</summary>
    internal sealed record TrendItem(
        string? Title,
        string? Category,
        string? Description,
        string? TrendName,
        string? ScenePrompt,
        string? Script,
        string? AspectRatio,
        int? DurationSeconds,
        bool? HasPerson,
        IReadOnlyList<string>? SourceUrls);
}
