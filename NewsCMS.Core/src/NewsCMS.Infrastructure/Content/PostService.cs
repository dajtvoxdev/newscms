using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Application.Content.Dtos;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Content;

public sealed class PostService : IPostService
{
    private readonly AppDbContext _db;

    public PostService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedList<PostListItemDto>> SearchAsync(string? keyword, Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = PublishedPosts();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x => x.Title.Contains(keyword) || (x.Excerpt != null && x.Excerpt.Contains(keyword)));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == categoryId.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PostListItemDto(
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.PublishedAt,
                x.Category.Name,
                x.Category.Slug,
                x.FeaturedImage == null ? null : x.FeaturedImage.FilePath))
            .ToListAsync(ct);

        return new PagedList<PostListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public Task<PostDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        PublishedPosts()
            .Where(x => x.Id == id)
            .Select(x => ToDto(x))
            .FirstOrDefaultAsync(ct);

    public Task<PostDto?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        PublishedPosts()
            .Where(x => x.Slug == slug)
            .Select(x => ToDto(x))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PostListItemDto>> GetFeaturedAsync(int take = 6, CancellationToken ct = default) =>
        await PublishedPosts()
            .Where(x => x.IsFeatured)
            .OrderByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(x => new PostListItemDto(
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.PublishedAt,
                x.Category.Name,
                x.Category.Slug,
                x.FeaturedImage == null ? null : x.FeaturedImage.FilePath))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PostListItemDto>> GetMostReadAsync(int take = 10, CancellationToken ct = default) =>
        await PublishedPosts()
            .OrderByDescending(x => x.ViewCount)
            .ThenByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(x => new PostListItemDto(
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.PublishedAt,
                x.Category.Name,
                x.Category.Slug,
                x.FeaturedImage == null ? null : x.FeaturedImage.FilePath))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PostListItemDto>> GetByCategoryAsync(string categorySlug, int take = 10, CancellationToken ct = default) =>
        await PublishedPosts()
            .Where(x => x.Category.Slug == categorySlug)
            .OrderByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(x => new PostListItemDto(
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.PublishedAt,
                x.Category.Name,
                x.Category.Slug,
                x.FeaturedImage == null ? null : x.FeaturedImage.FilePath))
            .ToListAsync(ct);

    public Task<Result<Guid>> CreateAsync(PostCreateDto dto, CancellationToken ct = default) =>
        Task.FromResult(Result<Guid>.Failure("Post writing is not enabled in this theme runtime."));

    public Task<Result> UpdateAsync(PostUpdateDto dto, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure("Post writing is not enabled in this theme runtime."));

    public Task<Result> DeleteAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure("Post writing is not enabled in this theme runtime."));

    public Task<Result> PublishAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure("Post writing is not enabled in this theme runtime."));

    public async Task IncreaseViewAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (post is null)
        {
            return;
        }

        post.ViewCount += 1;
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable<NewsCMS.Domain.Entities.Content.Post> PublishedPosts() =>
        _db.Posts
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.FeaturedImage)
            .Where(x => x.Status == PostStatus.Published && x.PublishedAt != null && x.Category.IsActive);

    private static PostDto ToDto(NewsCMS.Domain.Entities.Content.Post x) =>
        new(
            x.Id,
            x.Title,
            x.Slug,
            x.Excerpt,
            x.Content,
            x.Status,
            x.PublishedAt,
            x.ViewCount,
            x.IsFeatured,
            x.CategoryId,
            x.Category.Name,
            x.Category.Slug,
            x.FeaturedImageId,
            x.FeaturedImage == null ? null : x.FeaturedImage.FilePath,
            Array.Empty<string>());
}
