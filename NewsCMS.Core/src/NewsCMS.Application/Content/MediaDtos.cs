using NewsCMS.Application.Common;

namespace NewsCMS.Application.Content;

public sealed record MediaListItemDto(
    Guid Id,
    string FileName,
    string FilePath,
    string Kind,
    long Size,
    string? Title,
    string? AltText,
    int? Width,
    int? Height,
    Guid? FolderId,
    DateTime CreatedAt,
    string? PosterUrl = null);

public sealed record MediaDetailDto(
    Guid Id,
    string FileName,
    string FilePath,
    string StorageKey,
    string Kind,
    string MimeType,
    long Size,
    string? Title,
    string? AltText,
    int? Width,
    int? Height,
    Guid? FolderId,
    string? FolderName,
    Guid UploadedById,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record MediaUploadResultDto(
    Guid Id,
    string Location,
    string FileName,
    long Size,
    int? Width,
    int? Height,
    string Kind,
    string? PosterUrl = null);

public sealed record MediaUpdateDto(string? Title, string? AltText, Guid? FolderId);

public sealed record MediaFolderDto(Guid Id, string Name, Guid? ParentId);

public interface IMediaService
{
    Task<PagedList<MediaListItemDto>> SearchAsync(
        string? keyword,
        MediaKind? kind,
        Guid? folderId,
        bool includeDeleted,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<MediaDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Tra media theo đường dẫn công khai (FilePath) — dùng cho form chỉ bind URL ảnh.</summary>
    Task<MediaDetailDto?> GetByPathAsync(string filePath, CancellationToken ct);
    Task<Result> UpdateAsync(Guid id, MediaUpdateDto dto, CancellationToken ct);

    Task<Result<MediaUploadResultDto>> CreateFromUploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        long size,
        Guid userId,
        Guid? folderId,
        CancellationToken ct);

    Task<Result> SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<Result> RestoreAsync(Guid id, CancellationToken ct);
    Task<Result> PurgeAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<MediaFolderDto>> GetFoldersAsync(CancellationToken ct);
    Task<Result<MediaFolderDto>> CreateFolderAsync(string name, Guid? parentId, CancellationToken ct);
    Task<Result> RenameFolderAsync(Guid id, string name, CancellationToken ct);
    Task<Result> DeleteFolderAsync(Guid id, CancellationToken ct);
}
