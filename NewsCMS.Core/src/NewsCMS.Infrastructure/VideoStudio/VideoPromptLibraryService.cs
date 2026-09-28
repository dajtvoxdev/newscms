using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>Đọc/ghi kho mẫu brief và cấu hình của nó.</summary>
public sealed class VideoPromptLibraryService : IVideoPromptLibraryService
{
    private readonly AppDbContext _db;

    public VideoPromptLibraryService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<VideoPromptTemplateDto>> GetPublishedAsync(CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;

        List<VideoPromptTemplate> rows = await _db.VideoPromptTemplates.AsNoTracking()
            .Where(x => x.Status == VideoPromptTemplateStatus.Published && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(ct);

        // Trend mới nhất trước (đó là lý do có kho trend), rồi mẫu hay dùng, rồi thứ tự quản trị đặt.
        return rows
            .OrderByDescending(x => x.Source == VideoPromptTemplateSource.Trend)
            .ThenByDescending(x => x.Source == VideoPromptTemplateSource.Trend ? x.CreatedAt : DateTime.MinValue)
            .ThenByDescending(x => x.UsageCount)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<VideoPromptTemplateDto>> GetAllAsync(string? source = null, string? status = null, CancellationToken ct = default)
    {
        IQueryable<VideoPromptTemplate> query = _db.VideoPromptTemplates.AsNoTracking();

        if (ParseSource(source) is { } s)
        {
            query = query.Where(x => x.Source == s);
        }

        if (ParseStatus(status) is { } st)
        {
            query = query.Where(x => x.Status == st);
        }

        List<VideoPromptTemplate> rows = await query.ToListAsync(ct);

        return rows
            .OrderBy(x => x.Status == VideoPromptTemplateStatus.PendingReview ? 0 : 1)
            .ThenByDescending(x => x.Source == VideoPromptTemplateSource.Trend ? x.CreatedAt : DateTime.MinValue)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .Select(ToDto)
            .ToList();
    }

    public async Task<VideoPromptTemplateDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        VideoPromptTemplate? row = await _db.VideoPromptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        return row is null ? null : ToDto(row);
    }

    public async Task<Result<VideoPromptTemplateDto>> CreateAsync(VideoPromptTemplateInput input, CancellationToken ct = default)
    {
        List<string> errors = VideoPromptTemplateRules.Validate(input);

        if (errors.Count > 0)
        {
            return Result<VideoPromptTemplateDto>.Failure(string.Join(" ", errors));
        }

        var row = new VideoPromptTemplate
        {
            Source = VideoPromptTemplateSource.Manual,
            Status = VideoPromptTemplateStatus.Published,
        };
        Apply(row, input);

        _db.VideoPromptTemplates.Add(row);
        await _db.SaveChangesAsync(ct);

        return Result<VideoPromptTemplateDto>.Success(ToDto(row));
    }

    public async Task<Result<VideoPromptTemplateDto>> UpdateAsync(Guid id, VideoPromptTemplateInput input, CancellationToken ct = default)
    {
        VideoPromptTemplate? row = await _db.VideoPromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return Result<VideoPromptTemplateDto>.Failure("Mẫu không còn trong kho.");
        }

        List<string> errors = VideoPromptTemplateRules.Validate(input);

        if (errors.Count > 0)
        {
            return Result<VideoPromptTemplateDto>.Failure(string.Join(" ", errors));
        }

        Apply(row, input);
        await _db.SaveChangesAsync(ct);

        return Result<VideoPromptTemplateDto>.Success(ToDto(row));
    }

    public async Task<Result> SetStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (ParseStatus(status) is not { } target)
        {
            return Result.Failure($"Trạng thái \"{status}\" không hợp lệ.");
        }

        VideoPromptTemplate? row = await _db.VideoPromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return Result.Failure("Mẫu không còn trong kho.");
        }

        row.Status = target;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        VideoPromptTemplate? row = await _db.VideoPromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return Result.Failure("Mẫu không còn trong kho.");
        }

        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task RecordUsageAsync(Guid id, CancellationToken ct = default)
    {
        // Cộng trong SQL: hai người tạo video cùng lúc từ một mẫu không làm mất lượt đếm.
        if (_db.Database.IsRelational())
        {
            await _db.VideoPromptTemplates.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsageCount, x => x.UsageCount + 1), ct);
            return;
        }

        VideoPromptTemplate? row = await _db.VideoPromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is not null)
        {
            row.UsageCount++;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<VideoPromptLibrarySettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        VideoPromptLibrarySettings row = await _db.VideoPromptLibrarySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct)
            ?? new VideoPromptLibrarySettings();

        return new VideoPromptLibrarySettingsDto(
            row.TrendAutoUpdateEnabled, row.TrendIntervalHours, row.TemplatesPerRun, row.TrendLifetimeDays, row.RequireReview, row.Focus);
    }

    public async Task<Result> SaveSettingsAsync(VideoPromptLibrarySettingsDto input, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (input.TrendIntervalHours is < 6 or > 24 * 30) errors.Add("Chu kỳ cập nhật 6 giờ – 30 ngày.");
        if (input.TemplatesPerRun is < 1 or > 20) errors.Add("Số mẫu mỗi lần 1–20.");
        if (input.TrendLifetimeDays is < 1 or > 180) errors.Add("Hạn dùng mẫu trend 1–180 ngày.");
        if (input.Focus?.Length > 1000) errors.Add("Trọng tâm dài quá 1000 ký tự.");

        if (errors.Count > 0)
        {
            return Result.Failure(string.Join(" ", errors));
        }

        VideoPromptLibrarySettings? row = await _db.VideoPromptLibrarySettings.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);

        if (row is null)
        {
            row = new VideoPromptLibrarySettings();
            _db.VideoPromptLibrarySettings.Add(row);
        }

        row.TrendAutoUpdateEnabled = input.TrendAutoUpdateEnabled;
        row.TrendIntervalHours = input.TrendIntervalHours;
        row.TemplatesPerRun = input.TemplatesPerRun;
        row.TrendLifetimeDays = input.TrendLifetimeDays;
        row.RequireReview = input.RequireReview;
        row.Focus = string.IsNullOrWhiteSpace(input.Focus) ? null : input.Focus.Trim();

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<IReadOnlyList<VideoPromptTrendRunDto>> GetRunsAsync(int take = 10, CancellationToken ct = default)
    {
        List<VideoPromptTrendRun> rows = await _db.VideoPromptTrendRuns.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    // ------------------------------------------------------------------

    internal static VideoPromptTemplateDto ToDto(VideoPromptTemplate x) => new(
        x.Id,
        x.Title,
        x.Category,
        x.Description,
        x.ScenePrompt,
        x.ScriptTemplate,
        x.AspectRatio,
        x.DurationSeconds,
        x.HasPerson,
        SourceName(x.Source),
        StatusName(x.Status),
        x.TrendName,
        (x.SourceUrls ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        x.ExpiresAt,
        x.UsageCount,
        x.SortOrder,
        x.CreatedAt);

    internal static VideoPromptTrendRunDto ToDto(VideoPromptTrendRun x) => new(
        x.Id,
        x.Trigger,
        x.Status switch
        {
            VideoPromptTrendRunStatus.Running => "running",
            VideoPromptTrendRunStatus.Succeeded => "succeeded",
            _ => "failed",
        },
        x.StartedAt,
        x.FinishedAt,
        x.Added,
        x.Rejected,
        x.Expired,
        x.UsedWebSearch,
        x.Notes);

    private static void Apply(VideoPromptTemplate row, VideoPromptTemplateInput input)
    {
        row.Title = input.Title!.Trim();
        row.Category = input.Category!.Trim();
        row.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        row.ScenePrompt = input.ScenePrompt!.Trim();
        row.ScriptTemplate = input.ScriptTemplate!.Trim();
        row.AspectRatio = input.AspectRatio!;
        row.DurationSeconds = input.DurationSeconds;
        row.HasPerson = input.HasPerson;
        row.SortOrder = input.SortOrder;
    }

    private static string SourceName(VideoPromptTemplateSource source) => source switch
    {
        VideoPromptTemplateSource.Default => "default",
        VideoPromptTemplateSource.Manual => "manual",
        _ => "trend",
    };

    private static string StatusName(VideoPromptTemplateStatus status) => status switch
    {
        VideoPromptTemplateStatus.Published => "published",
        VideoPromptTemplateStatus.PendingReview => "pending_review",
        _ => "hidden",
    };

    private static VideoPromptTemplateSource? ParseSource(string? value) => value switch
    {
        "default" => VideoPromptTemplateSource.Default,
        "manual" => VideoPromptTemplateSource.Manual,
        "trend" => VideoPromptTemplateSource.Trend,
        _ => null,
    };

    private static VideoPromptTemplateStatus? ParseStatus(string? value) => value switch
    {
        "published" => VideoPromptTemplateStatus.Published,
        "pending_review" => VideoPromptTemplateStatus.PendingReview,
        "hidden" => VideoPromptTemplateStatus.Hidden,
        _ => null,
    };
}
