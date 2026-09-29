using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Content;

public class Media : BaseEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string FileName { get; set; } = default!;
    public string StorageKey { get; set; } = default!;
    public string FilePath { get; set; } = default!;
    public string MimeType { get; set; } = default!;
    public string Kind { get; set; } = "other";
    public long Size { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    // Poster video sinh bằng ffmpeg lúc upload xong (chỉ Kind = "video").
    // PosterUrl để render <video poster>; PosterStorageKey phục vụ xoá/trash.
    public string? PosterUrl { get; set; }
    public string? PosterStorageKey { get; set; }

    public Guid? FolderId { get; set; }
    public MediaFolder? Folder { get; set; }
    public Guid UploadedById { get; set; }

    /// <summary>Nguồn gốc file — xem <see cref="MediaOrigins"/>. Ảnh AI phải truy ngược được (nghĩa vụ gắn nhãn AI).</summary>
    public string Origin { get; set; } = MediaOrigins.Upload;

    /// <summary>Job của Xưởng ảnh AI đã sinh ra ảnh này (khi <see cref="Origin"/> là ảnh AI).</summary>
    public Guid? AiJobId { get; set; }

    public bool IsAiGenerated => Origin is MediaOrigins.AiGenerated or MediaOrigins.AiEdited;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class MediaFolder : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public MediaFolder? Parent { get; set; }
    public ICollection<MediaFolder> Children { get; set; } = new List<MediaFolder>();
    public ICollection<Media> Items { get; set; } = new List<Media>();
}

public static class MediaOrigins
{
    public const string Upload = "upload";

    /// <summary>Ảnh tạo mới bằng AI.</summary>
    public const string AiGenerated = "ai-generated";

    /// <summary>Ảnh có sẵn được sửa bằng AI.</summary>
    public const string AiEdited = "ai-edited";
}
