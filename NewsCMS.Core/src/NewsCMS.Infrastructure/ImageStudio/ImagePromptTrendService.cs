using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Ai.PromptLibrary;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Ai.PromptLibrary;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// Cập nhật kho mẫu ảnh theo xu hướng: gọi skill <c>imagestudio_trend_templates</c> (kèm công cụ tìm web nếu
/// đã bật), kiểm từng mẫu, lưu mẫu đạt; lần chạy theo lịch còn tự tạo ảnh demo cho mẫu mới nếu được bật.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mẫu AI không được tin mặc định.</b> Mỗi mẫu qua đúng luật của mẫu tự viết
/// (<see cref="ImagePromptTemplateRules"/>: chỗ giữ theo mục đích, quảng cáo tuyệt đối, ngành nhạy cảm,
/// từ bị chặn), thêm luật không trùng tiêu đề (so sau khi bỏ dấu).
/// </para>
/// <para>
/// <b>Không bao giờ chạy chồng</b>: khoá trong tiến trình + kiểm lần chạy dở dưới 30 phút trong DB.
/// </para>
/// <para>
/// <b>Ảnh demo chỉ tự tạo khi chạy theo lịch</b> (ở nền). Bấm "Cập nhật ngay" thì trả lời nhanh, quản
/// trị bấm tiếp "Tạo demo cho mọi mẫu chưa có" nếu muốn — không giữ request HTTP chờ vài phút.
/// </para>
/// </remarks>
public sealed class ImagePromptTrendService : IImagePromptTrendService
{
    public const string ScheduleTrigger = "schedule";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan StaleRun = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IAiCompletionService _ai;
    private readonly ImagePromptDemoService _demos;
    private readonly ILogger<ImagePromptTrendService> _logger;

    public ImagePromptTrendService(AppDbContext db, IAiCompletionService ai, ImagePromptDemoService demos, ILogger<ImagePromptTrendService> logger)
    {
        _db = db;
        _ai = ai;
        _demos = demos;
        _logger = logger;
    }

    public async Task<bool> IsDueAsync(CancellationToken ct = default)
    {
        ImagePromptLibrarySettings settings = await ImagePromptLibraryService.LoadSettingsAsync(_db, ct);

        if (!settings.TrendAutoUpdateEnabled)
        {
            return false;
        }

        DateTime? last = await _db.ImagePromptTrendRuns.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Select(x => (DateTime?)x.StartedAt)
            .FirstOrDefaultAsync(ct);

        return TrendRunSupport.IsDue(true, settings.TrendIntervalHours, last, DateTime.UtcNow);
    }

    public async Task<Result<ImagePromptTrendRunDto>> RefreshAsync(string trigger, CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct))
        {
            return Result<ImagePromptTrendRunDto>.Failure("Đang có một lần cập nhật chạy. Đợi xong rồi thử lại.");
        }

        try
        {
            DateTime now = DateTime.UtcNow;

            if (await _db.ImagePromptTrendRuns.AnyAsync(x => x.Status == ImagePromptTrendRunStatus.Running && x.StartedAt > now - StaleRun, ct))
            {
                return Result<ImagePromptTrendRunDto>.Failure("Đang có một lần cập nhật chạy. Đợi xong rồi thử lại.");
            }

            ImagePromptLibrarySettings settings = await ImagePromptLibraryService.LoadSettingsAsync(_db, ct);

            var run = new ImagePromptTrendRun
            {
                Trigger = TrendRunSupport.Truncate(trigger, 200),
                Status = ImagePromptTrendRunStatus.Running,
                StartedAt = now,
                UsedWebSearch = await TrendRunSupport.HasWebSearchAsync(_db, AiTaskKeys.ImageStudioTrendTemplates, ct),
            };
            _db.ImagePromptTrendRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            List<Guid> added = [];

            try
            {
                added = await ExecuteAsync(run, settings, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cập nhật kho mẫu ảnh theo trend lỗi (run {RunId}).", run.Id);
                run.Status = ImagePromptTrendRunStatus.Failed;
                run.Notes = TrendRunSupport.Append(run.Notes, $"Lỗi: {ex.Message}");
            }

            if (run.Status == ImagePromptTrendRunStatus.Succeeded && added.Count > 0 && settings.AutoDemoForTrend && trigger == ScheduleTrigger)
            {
                await CreateDemosAsync(run, added.Take(Math.Clamp(settings.MaxAutoDemosPerRun, 0, 20)).ToList(), ct);
            }

            run.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            ImagePromptTrendRunDto dto = ImagePromptLibraryService.ToDto(run);

            return run.Status == ImagePromptTrendRunStatus.Succeeded
                ? Result<ImagePromptTrendRunDto>.Success(dto)
                : Result<ImagePromptTrendRunDto>.Failure(run.Notes ?? "Cập nhật thất bại.");
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<List<Guid>> ExecuteAsync(ImagePromptTrendRun run, ImagePromptLibrarySettings settings, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        // 1. Mẫu trend hết hạn: ẩn đi (vẫn giữ trong kho để quản trị xem lại).
        List<ImagePromptTemplate> expired = await _db.ImagePromptTemplates
            .Where(x => x.Source == ImagePromptTemplateSource.Trend
                        && x.Status != ImagePromptTemplateStatus.Hidden
                        && x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (ImagePromptTemplate template in expired)
        {
            template.Status = ImagePromptTemplateStatus.Hidden;
        }

        run.Expired = expired.Count;
        await _db.SaveChangesAsync(ct);

        // 2. Xin AI mẫu mới.
        List<string> existingTitles = await _db.ImagePromptTemplates.AsNoTracking()
            .Where(x => x.Status != ImagePromptTemplateStatus.Hidden)
            .Select(x => x.Title)
            .ToListAsync(ct);

        Result<AiGenerationResult> generated = await _ai.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.ImageStudioTrendTemplates,
            AiGenerationStyle.Plain,
            Title: null,
            Content: BuildRequest(settings, existingTitles, now),
            Selection: null,
            Language: "vi",
            ConnectionId: null), ct);

        if (!generated.Succeeded)
        {
            run.Status = ImagePromptTrendRunStatus.Failed;
            run.Notes = TrendRunSupport.Append(run.Notes,
                $"AI không trả lời: {generated.Error} Kiểm tra kết nối AI mặc định và skill \"{AiTaskKeys.ImageStudioTrendTemplates}\" ở trang AI.");
            return [];
        }

        List<TrendItem>? items = TrendRunSupport.ParseArray<TrendItem>(generated.Value!.Content, out string? parseError);

        if (items is null)
        {
            run.Status = ImagePromptTrendRunStatus.Failed;
            run.Notes = TrendRunSupport.Append(run.Notes, $"Không đọc được kết quả của AI: {parseError}");
            return [];
        }

        // 3. Kiểm tra từng mẫu, lưu mẫu đạt.
        List<string> blocked = ImagePromptTemplateRules.ParseBlockedTerms(settings.BlockedTerms);
        HashSet<string> seen = existingTitles.Select(AdContentRules.NormalizeTitle).ToHashSet(StringComparer.Ordinal);
        int limit = Math.Clamp(settings.TemplatesPerRun, 1, 20);
        var addedRows = new List<ImagePromptTemplate>();

        foreach (TrendItem item in items)
        {
            if (run.Added >= limit)
            {
                break;
            }

            string title = item.Title?.Trim() ?? string.Empty;
            ImagePurpose? purpose = ImagePurposes.Parse(item.Purpose);
            var input = new ImagePromptTemplateInput(
                title,
                ImagePromptTemplateRules.Categories.Contains(item.Category?.Trim() ?? string.Empty) ? item.Category!.Trim() : "Khác",
                purpose ?? ImagePurpose.Free,
                item.Description,
                item.Prompt,
                ImageStudioRules.AspectRatios.Contains(item.AspectRatio ?? string.Empty) ? item.AspectRatio : "1:1",
                0);

            (List<string> errors, _) = ImagePromptTemplateRules.Validate(input, blocked);

            if (purpose is null)
            {
                errors.Add($"Mục đích \"{item.Purpose}\" không hợp lệ.");
            }

            if (title.Length > 0 && !seen.Add(AdContentRules.NormalizeTitle(title)))
            {
                errors.Add("Trùng tiêu đề mẫu đã có.");
            }

            if (errors.Count > 0)
            {
                run.Rejected++;
                run.Notes = TrendRunSupport.Append(run.Notes, $"Loại \"{TrendRunSupport.Truncate(title, 80)}\": {string.Join(" ", errors)}");
                continue;
            }

            var row = new ImagePromptTemplate
            {
                Title = title,
                Category = input.Category!,
                Purpose = input.Purpose,
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : TrendRunSupport.Truncate(input.Description.Trim(), 500),
                Prompt = input.Prompt!.Trim(),
                AspectRatio = input.AspectRatio!,
                Source = ImagePromptTemplateSource.Trend,
                Status = settings.RequireReview ? ImagePromptTemplateStatus.PendingReview : ImagePromptTemplateStatus.Published,
                TrendName = string.IsNullOrWhiteSpace(item.TrendName) ? null : TrendRunSupport.Truncate(item.TrendName.Trim(), 200),
                SourceUrls = TrendRunSupport.CleanUrls(item.SourceUrls),
                ExpiresAt = now.AddDays(Math.Clamp(settings.TrendLifetimeDays, 1, 180)),
                TrendRunId = run.Id,
            };
            _db.ImagePromptTemplates.Add(row);
            addedRows.Add(row);
            run.Added++;
        }

        if (!run.UsedWebSearch)
        {
            run.Notes = TrendRunSupport.Append(run.Notes, "Chưa bật công cụ tìm web — xu hướng chỉ dựa trên hiểu biết của model, có thể không mới.");
        }

        run.Status = ImagePromptTrendRunStatus.Succeeded;
        await _db.SaveChangesAsync(ct);

        return addedRows.Select(r => r.Id).ToList();
    }

    private async Task CreateDemosAsync(ImagePromptTrendRun run, List<Guid> templateIds, CancellationToken ct)
    {
        if (templateIds.Count == 0)
        {
            return;
        }

        try
        {
            Result<ImageDemoBatchResultDto> result = await _demos.GenerateManyAsync(templateIds, ct);

            if (result.Succeeded)
            {
                run.DemosCreated = result.Value!.Created;

                if (result.Value.Notes is { } notes)
                {
                    run.Notes = TrendRunSupport.Append(run.Notes, $"Ảnh demo: {notes}");
                }
            }
            else
            {
                run.Notes = TrendRunSupport.Append(run.Notes, $"Không tạo được ảnh demo: {result.Error}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mẫu đã lưu xong — demo lỗi không làm hỏng lần chạy.
            _logger.LogWarning(ex, "Tự tạo ảnh demo cho mẫu trend lỗi (run {RunId}).", run.Id);
            run.Notes = TrendRunSupport.Append(run.Notes, $"Không tạo được ảnh demo: {ex.Message}");
        }
    }

    private static string BuildRequest(ImagePromptLibrarySettings settings, IReadOnlyList<string> existingTitles, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hôm nay là {now:yyyy-MM-dd}.");
        sb.AppendLine($"Cần {Math.Clamp(settings.TemplatesPerRun, 1, 20)} mẫu mới, mỗi mẫu một phong cách khác nhau, trải đều các mục đích (ảnh bìa bài viết, ảnh trong bài, ảnh sản phẩm, mạng xã hội).");

        if (!string.IsNullOrWhiteSpace(settings.Focus))
        {
            sb.AppendLine($"Trọng tâm: {settings.Focus}");
        }

        List<string> blocked = ImagePromptTemplateRules.ParseBlockedTerms(settings.BlockedTerms);

        if (blocked.Count > 0)
        {
            sb.AppendLine($"Không dùng các từ: {string.Join(", ", blocked)}.");
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
        string? Purpose,
        string? Description,
        string? TrendName,
        string? Prompt,
        string? AspectRatio,
        IReadOnlyList<string>? SourceUrls);
}
