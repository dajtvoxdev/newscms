namespace NewsCMS.Web.Areas.Admin.Shared;

/// <summary>Tham số init TinyMCE để truyền vào partial _TinyMce.cshtml.</summary>
public class TinyMceOptions
{
    /// <summary>CSS selector của textarea, ví dụ "#post-content".</summary>
    public string Selector { get; set; } = "textarea.tinymce";
    public int Height { get; set; } = 500;

    /// <summary>
    /// Loại nội dung cho trợ lý AI: "post" hoặc "product". Chỉ đổi nhãn hiển thị
    /// (Tiêu đề/Tóm tắt/Nội dung so với Tên sản phẩm/Mô tả ngắn/Mô tả chi tiết).
    /// </summary>
    public string AiContentType { get; set; } = "post";

    /// <summary>Selector ô tiêu đề (bài viết) hoặc tên (sản phẩm) để AI đọc và ghi vào.</summary>
    public string? AiTitleSelector { get; set; }

    /// <summary>Selector ô tóm tắt (bài viết) hoặc mô tả ngắn (sản phẩm).</summary>
    public string? AiExcerptSelector { get; set; }
}
