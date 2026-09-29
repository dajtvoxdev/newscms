using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>Bật Xưởng ảnh cho từng site + hạn mức — nhìn mọi site nên bỏ filter site có chủ đích.</summary>
public sealed class ImageStudioSiteService : IImageStudioSiteService
{
    public const int MaxQuota = 1_000_000;

    private readonly AppDbContext _db;

    public ImageStudioSiteService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ImageStudioSiteRowDto>> GetSitesAsync(CancellationToken ct = default)
    {
        var sites = await _db.Sites.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Slug })
            .ToListAsync(ct);

        Dictionary<Guid, ImageStudioSiteSettings> settings = await _db.ImageStudioSiteSettings.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(s => s.SiteId, ct);

        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var usage = await _db.ImageProviderCalls.AsNoTracking()
            .Where(c => c.CreatedAt >= monthStart)
            .GroupBy(c => c.SiteId)
            .Select(g => new { SiteId = g.Key, Images = g.Sum(c => c.ImagesReturned), Cost = g.Sum(c => c.CostUsd) })
            .ToDictionaryAsync(x => x.SiteId, ct);

        return sites.Select(s =>
        {
            ImageStudioSiteSettings? row = settings.GetValueOrDefault(s.Id);
            var used = usage.GetValueOrDefault(s.Id);

            return new ImageStudioSiteRowDto(
                s.Id,
                s.Name,
                s.Slug,
                row?.Enabled ?? false,
                row?.MonthlyImageQuota ?? new ImageStudioSiteSettings().MonthlyImageQuota,
                row?.PerUserDailyQuota ?? new ImageStudioSiteSettings().PerUserDailyQuota,
                used?.Images ?? 0,
                used?.Cost ?? 0);
        }).ToList();
    }

    public async Task<Result> SaveAsync(ImageStudioSiteQuotaInput input, CancellationToken ct = default)
    {
        if (input.MonthlyImageQuota is < 0 or > MaxQuota || input.PerUserDailyQuota is < 0 or > MaxQuota)
        {
            return Result.Failure("Hạn mức phải từ 0 (không giới hạn) đến 1.000.000.");
        }

        bool siteExists = await _db.Sites.IgnoreQueryFilters().AnyAsync(s => s.Id == input.SiteId, ct);

        if (!siteExists)
        {
            return Result.Failure("Không tìm thấy site.");
        }

        ImageStudioSiteSettings? row = await _db.ImageStudioSiteSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.SiteId == input.SiteId, ct);

        if (row is null)
        {
            row = new ImageStudioSiteSettings { SiteId = input.SiteId };
            _db.ImageStudioSiteSettings.Add(row);
        }

        row.Enabled = input.Enabled;
        row.MonthlyImageQuota = input.MonthlyImageQuota;
        row.PerUserDailyQuota = input.PerUserDailyQuota;

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
