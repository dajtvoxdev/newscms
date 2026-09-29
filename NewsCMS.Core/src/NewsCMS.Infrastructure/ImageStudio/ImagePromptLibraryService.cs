using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>Đọc/ghi kho mẫu prompt ảnh và cấu hình của nó.</summary>
public sealed class ImagePromptLibraryService : IImagePromptLibraryService
{
    private readonly AppDbContext _db;

    public ImagePromptLibraryService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ImagePromptTemplateDto>> GetPublishedAsync(ImagePurpose? purpose = null, CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;

        IQueryable<ImagePromptTemplate> query = _db.ImagePromptTemplates.AsNoTracking()
            .Where(x => x.Status == ImagePromptTemplateStatus.Published
                        && (x.ExpiresAt == null || x.ExpiresAt > now)
                        && !x.RequiresSourceImage);

        if (purpose is { } p && p != ImagePurpose.Free)
        {
            query = query.Where(x => x.Purpose == p || x.Purpose == ImagePurpose.Free);
        }

        List<ImagePromptTemplate> rows = await query.ToListAsync(ct);

        // Đúng mục đích trước, rồi trend mới nhất, mẫu hay dùng, thứ tự quản trị đặt.
        return rows
            .OrderByDescending(x => purpose is { } want && x.Purpose == want)
            .ThenByDescending(x => x.Source == ImagePromptTemplateSource.Trend)
            .ThenByDescending(x => x.Source == ImagePromptTemplateSource.Trend ? x.CreatedAt : DateTime.MinValue)
            .ThenByDescending(x => x.UsageCount)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<ImagePromptTemplateDto>> GetAllAsync(string? source = null, string? status = null, CancellationToken ct = default)
    {
        IQueryable<ImagePromptTemplate> query = _db.ImagePromptTemplates.AsNoTracking();

        if (ParseSource(source) is { } s)
        {
            query = query.Where(x => x.Source == s);
        }

        if (ParseStatus(status) is { } st)
        {
            query = query.Where(x => x.Status == st);
        }

        List<ImagePromptTemplate> rows = await query.ToListAsync(ct);

        return rows
            .OrderBy(x => x.Status == ImagePromptTemplateStatus.PendingReview ? 0 : 1)
            .ThenByDescending(x => x.Source == ImagePromptTemplateSource.Trend ? x.CreatedAt : DateTime.MinValue)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .Select(ToDto)
            .ToList();
    }

    public async Task<ImagePromptTemplateDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        ImagePromptTemplate? row = await _db.ImagePromptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        return row is null ? null : ToDto(row);
    }

    public async Task<Result<ImagePromptTemplateSaved>> CreateAsync(ImagePromptTemplateInput input, CancellationToken ct = default)
    {
        (List<string> errors, List<string> warnings) = ImagePromptTemplateRules.Validate(input, await BlockedTermsAsync(ct));

        if (errors.Count > 0)
        {
            return Result<ImagePromptTemplateSaved>.Failure(string.Join(" ", errors));
        }

        var row = new ImagePromptTemplate
        {
            Source = ImagePromptTemplateSource.Manual,
            Status = ImagePromptTemplateStatus.Published,
        };
        Apply(row, input);

        _db.ImagePromptTemplates.Add(row);
        await _db.SaveChangesAsync(ct);

        return Result<ImagePromptTemplateSaved>.Success(new ImagePromptTemplateSaved(ToDto(row), warnings));
    }

    public async Task<Result<ImagePromptTemplateSaved>> UpdateAsync(Guid id, ImagePromptTemplateInput input, CancellationToken ct = default)
    {
        ImagePromptTemplate? row = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return Result<ImagePromptTemplateSaved>.Failure("Mẫu không còn trong kho.");
        }

        (List<string> errors, List<string> warnings) = ImagePromptTemplateRules.Validate(input, await BlockedTermsAsync(ct));

        if (errors.Count > 0)
        {
            return Result<ImagePromptTemplateSaved>.Failure(string.Join(" ", errors));
        }

        Apply(row, input);
        await _db.SaveChangesAsync(ct);

        return Result<ImagePromptTemplateSaved>.Success(new ImagePromptTemplateSaved(ToDto(row), warnings));
    }

    public async Task<Result> SetStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (ParseStatus(status) is not { } target)
        {
            return Result.Failure($"Trạng thái \"{status}\" không hợp lệ.");
        }

        ImagePromptTemplate? row = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

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
        ImagePromptTemplate? row = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return Result.Failure("Mẫu không còn trong kho.");
        }

        // Xoá mềm; ảnh demo giữ lại để còn khôi phục được.
        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task RecordUsageAsync(Guid id, CancellationToken ct = default) => RecordUsageAsync(_db, id, ct);

    /// <summary>Cộng lượt dùng trong SQL: hai người tạo ảnh cùng lúc từ một mẫu không làm mất lượt đếm.</summary>
    internal static async Task RecordUsageAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.ImagePromptTemplates.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsageCount, x => x.UsageCount + 1), ct);
            return;
        }

        ImagePromptTemplate? row = await db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is not null)
        {
            row.UsageCount++;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<ImagePromptLibrarySettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        ImagePromptLibrarySettings row = await LoadSettingsAsync(_db, ct);

        return new ImagePromptLibrarySettingsDto(
            row.TrendAutoUpdateEnabled, row.TrendIntervalHours, row.TemplatesPerRun, row.TrendLifetimeDays, row.RequireReview,
            row.Focus, row.BlockedTerms, row.AutoDemoForTrend, row.MaxAutoDemosPerRun, row.DemoModelId);
    }

    public async Task<Result> SaveSettingsAsync(ImagePromptLibrarySettingsDto input, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (input.TrendIntervalHours is < 6 or > 24 * 30) errors.Add("Chu kỳ cập nhật 6 giờ – 30 ngày.");
        if (input.TemplatesPerRun is < 1 or > 20) errors.Add("Số mẫu mỗi lần 1–20.");
        if (input.TrendLifetimeDays is < 1 or > 180) errors.Add("Hạn dùng mẫu trend 1–180 ngày.");
        if (input.Focus?.Length > 1000) errors.Add("Trọng tâm dài quá 1000 ký tự.");
        if (input.BlockedTerms?.Length > 4000) errors.Add("Danh sách từ bị chặn dài quá 4000 ký tự.");
        if (input.MaxAutoDemosPerRun is < 0 or > 20) errors.Add("Số ảnh demo tự tạo mỗi lần 0–20.");

        if (input.DemoModelId is { } demoModel && !await _db.ImageModels.AnyAsync(m => m.Id == demoModel && m.IsActive, ct))
        {
            errors.Add("Model tạo demo không còn bật.");
        }

        if (errors.Count > 0)
        {
            return Result.Failure(string.Join(" ", errors));
        }

        ImagePromptLibrarySettings? row = await _db.ImagePromptLibrarySettings.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);

        if (row is null)
        {
            row = new ImagePromptLibrarySettings();
            _db.ImagePromptLibrarySettings.Add(row);
        }

        row.TrendAutoUpdateEnabled = input.TrendAutoUpdateEnabled;
        row.TrendIntervalHours = input.TrendIntervalHours;
        row.TemplatesPerRun = input.TemplatesPerRun;
        row.TrendLifetimeDays = input.TrendLifetimeDays;
        row.RequireReview = input.RequireReview;
        row.Focus = string.IsNullOrWhiteSpace(input.Focus) ? null : input.Focus.Trim();
        row.BlockedTerms = string.Join('\n', ImagePromptTemplateRules.ParseBlockedTerms(input.BlockedTerms));
        row.AutoDemoForTrend = input.AutoDemoForTrend;
        row.MaxAutoDemosPerRun = input.MaxAutoDemosPerRun;
        row.DemoModelId = input.DemoModelId;

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<IReadOnlyList<ImagePromptTrendRunDto>> GetRunsAsync(int take = 10, CancellationToken ct = default)
    {
        List<ImagePromptTrendRun> rows = await _db.ImagePromptTrendRuns.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    // ------------------------------------------------------------------

    internal static async Task<ImagePromptLibrarySettings> LoadSettingsAsync(AppDbContext db, CancellationToken ct) =>
        await db.ImagePromptLibrarySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct)
        ?? new ImagePromptLibrarySettings { BlockedTerms = ImagePromptTemplateRules.DefaultBlockedTerms };

    private async Task<List<string>> BlockedTermsAsync(CancellationToken ct) =>
        ImagePromptTemplateRules.ParseBlockedTerms((await LoadSettingsAsync(_db, ct)).BlockedTerms);

    internal static ImagePromptTemplateDto ToDto(ImagePromptTemplate x) => new(
        x.Id,
        x.Title,
        x.Category,
        x.Purpose,
        x.Description,
        x.Prompt,
        x.AspectRatio,
        x.RequiresSourceImage,
        x.RegionHint,
        SourceName(x.Source),
        StatusName(x.Status),
        x.TrendName,
        (x.SourceUrls ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        x.ExpiresAt,
        x.UsageCount,
        x.SortOrder,
        x.CreatedAt,
        x.DemoImageUrl,
        x.DemoSource,
        x.DemoUpdatedAt);

    internal static ImagePromptTrendRunDto ToDto(ImagePromptTrendRun x) => new(
        x.Id,
        x.Trigger,
        x.Status switch
        {
            ImagePromptTrendRunStatus.Running => "running",
            ImagePromptTrendRunStatus.Succeeded => "succeeded",
            _ => "failed",
        },
        x.StartedAt,
        x.FinishedAt,
        x.Added,
        x.Rejected,
        x.Expired,
        x.DemosCreated,
        x.UsedWebSearch,
        x.Notes);

    private static void Apply(ImagePromptTemplate row, ImagePromptTemplateInput input)
    {
        row.Title = input.Title!.Trim();
        row.Category = input.Category!.Trim();
        row.Purpose = input.Purpose;
        row.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        row.Prompt = input.Prompt!.Trim();
        row.AspectRatio = input.AspectRatio!;
        row.SortOrder = input.SortOrder;
        row.RequiresSourceImage = input.RequiresSourceImage;
        row.RegionHint = input.RegionHint;
    }

    private static string SourceName(ImagePromptTemplateSource source) => source switch
    {
        ImagePromptTemplateSource.Default => "default",
        ImagePromptTemplateSource.Manual => "manual",
        _ => "trend",
    };

    private static string StatusName(ImagePromptTemplateStatus status) => status switch
    {
        ImagePromptTemplateStatus.Published => "published",
        ImagePromptTemplateStatus.PendingReview => "pending_review",
        _ => "hidden",
    };

    private static ImagePromptTemplateSource? ParseSource(string? value) => value switch
    {
        "default" => ImagePromptTemplateSource.Default,
        "manual" => ImagePromptTemplateSource.Manual,
        "trend" => ImagePromptTemplateSource.Trend,
        _ => null,
    };

    private static ImagePromptTemplateStatus? ParseStatus(string? value) => value switch
    {
        "published" => ImagePromptTemplateStatus.Published,
        "pending_review" => ImagePromptTemplateStatus.PendingReview,
        "hidden" => ImagePromptTemplateStatus.Hidden,
        _ => null,
    };
}
