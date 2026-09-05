using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Site;

public class BannerService : IBannerService
{
    private readonly AppDbContext _db;
    public BannerService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<BannerDto>> GetByPositionAsync(string position, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Banners
            .AsNoTracking()
            .Where(b => b.Position == position
                     && b.IsActive
                     && (b.StartAt == null || b.StartAt <= now)
                     && (b.EndAt == null || b.EndAt >= now))
            .OrderBy(b => b.Order)
            .Select(b => new BannerDto(
                b.Id,
                b.Title,
                b.ImageId.ToString(),
                b.Url,
                b.Order))
            .ToListAsync(ct);
    }
}
