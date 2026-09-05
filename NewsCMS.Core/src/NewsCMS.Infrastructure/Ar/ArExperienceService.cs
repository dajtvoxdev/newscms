using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ar;
using NewsCMS.Application.Ar.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Ar;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ar;

public sealed class ArExperienceService : IArExperienceService
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;

    public ArExperienceService(AppDbContext db, SlugHelper slug)
    {
        _db = db;
        _slug = slug;
    }

    public async Task<PagedList<ArExperienceDto>> SearchAsync(
        string? keyword, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.ArExperiences.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x => x.Title.Contains(keyword) || x.Slug.Contains(keyword));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ArExperienceDto(
                x.Id, x.Title, x.Slug, x.Description,
                x.TargetImageUrl, x.MindFileUrl, x.VideoUrl,
                x.VideoWidth, x.VideoHeight,
                x.FallbackPostId, x.IsPublished, x.ViewCount,
                x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);

        return new PagedList<ArExperienceDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<ArExperienceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ArExperiences
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ArExperienceDto(
                x.Id, x.Title, x.Slug, x.Description,
                x.TargetImageUrl, x.MindFileUrl, x.VideoUrl,
                x.VideoWidth, x.VideoHeight,
                x.FallbackPostId, x.IsPublished, x.ViewCount,
                x.CreatedAt, x.UpdatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ArExperienceDetailDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var entity = await _db.ArExperiences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished, ct);

        if (entity is null) return null;

        string? fallbackPostTitle = null;
        string? fallbackPostContent = null;

        if (entity.FallbackPostId.HasValue)
        {
            var post = await _db.Posts
                .AsNoTracking()
                .Where(p => p.Id == entity.FallbackPostId)
                .Select(p => new { p.Title, p.Content })
                .FirstOrDefaultAsync(ct);

            fallbackPostTitle = post?.Title;
            fallbackPostContent = post?.Content;
        }

        return new ArExperienceDetailDto(
            entity.Id, entity.Title, entity.Slug, entity.Description,
            entity.TargetImageUrl, entity.MindFileUrl, entity.VideoUrl,
            entity.VideoWidth, entity.VideoHeight,
            fallbackPostTitle, fallbackPostContent);
    }

    public async Task<string?> GetFirstPublishedSlugAsync(CancellationToken ct = default)
    {
        return await _db.ArExperiences
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Slug)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result<Guid>> CreateAsync(ArExperienceUpsertDto dto, CancellationToken ct = default)
    {
        var slug = string.IsNullOrWhiteSpace(dto.Slug)
            ? _slug.Generate(dto.Title)
            : dto.Slug.Trim().ToLowerInvariant();

        if (await _db.ArExperiences.AnyAsync(x => x.Slug == slug, ct))
            return Result<Guid>.Failure("Slug đã tồn tại, vui lòng chọn slug khác.");

        if (string.IsNullOrWhiteSpace(dto.TargetImageUrl))
            return Result<Guid>.Failure("Vui lòng chọn ảnh target.");
        if (string.IsNullOrWhiteSpace(dto.MindFileUrl))
            return Result<Guid>.Failure("Vui lòng tải lên file .mind.");
        if (string.IsNullOrWhiteSpace(dto.VideoUrl))
            return Result<Guid>.Failure("Vui lòng chọn video.");

        var entity = new ArExperience
        {
            Title          = dto.Title.Trim(),
            Slug           = slug,
            Description    = dto.Description?.Trim(),
            TargetImageUrl = dto.TargetImageUrl,
            MindFileUrl    = dto.MindFileUrl,
            VideoUrl       = dto.VideoUrl,
            VideoWidth     = dto.VideoWidth > 0 ? dto.VideoWidth : 1.0,
            VideoHeight    = dto.VideoHeight > 0 ? dto.VideoHeight : 0.5625,
            FallbackPostId = dto.FallbackPostId,
            IsPublished    = dto.IsPublished
        };

        _db.ArExperiences.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(ArExperienceUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is null) return Result.Failure("Thiếu Id.");

        var entity = await _db.ArExperiences.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy trải nghiệm AR.");

        var slug = string.IsNullOrWhiteSpace(dto.Slug)
            ? _slug.Generate(dto.Title)
            : dto.Slug.Trim().ToLowerInvariant();

        if (await _db.ArExperiences.AnyAsync(x => x.Slug == slug && x.Id != dto.Id, ct))
            return Result.Failure("Slug đã tồn tại, vui lòng chọn slug khác.");

        entity.Title       = dto.Title.Trim();
        entity.Slug        = slug;
        entity.Description = dto.Description?.Trim();
        entity.VideoWidth  = dto.VideoWidth > 0 ? dto.VideoWidth : entity.VideoWidth;
        entity.VideoHeight = dto.VideoHeight > 0 ? dto.VideoHeight : entity.VideoHeight;
        entity.FallbackPostId = dto.FallbackPostId;
        entity.IsPublished = dto.IsPublished;
        entity.UpdatedAt   = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.TargetImageUrl))
            entity.TargetImageUrl = dto.TargetImageUrl;
        if (!string.IsNullOrWhiteSpace(dto.MindFileUrl))
            entity.MindFileUrl = dto.MindFileUrl;
        if (!string.IsNullOrWhiteSpace(dto.VideoUrl))
            entity.VideoUrl = dto.VideoUrl;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ArExperiences.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy trải nghiệm AR.");

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> TogglePublishAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ArExperiences.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy trải nghiệm AR.");

        entity.IsPublished = !entity.IsPublished;
        entity.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task IncrementViewAsync(Guid id, CancellationToken ct = default)
    {
        await _db.ArExperiences
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1), ct);
    }
}
