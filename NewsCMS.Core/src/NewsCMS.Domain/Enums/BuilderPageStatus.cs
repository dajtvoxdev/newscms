namespace NewsCMS.Domain.Enums;

/// <summary>Trạng thái xuất bản của trang builder (tách khỏi PostStatus để không ghép ngữ cảnh bài viết).</summary>
public enum BuilderPageStatus
{
    Draft = 0,
    Published = 1,
    Scheduled = 2
}
