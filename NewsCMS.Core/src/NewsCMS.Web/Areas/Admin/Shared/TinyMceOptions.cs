namespace NewsCMS.Web.Areas.Admin.Shared;

/// <summary>Tham số init TinyMCE để truyền vào partial _TinyMce.cshtml.</summary>
public class TinyMceOptions
{
    /// <summary>CSS selector của textarea, ví dụ "#post-content".</summary>
    public string Selector { get; set; } = "textarea.tinymce";
    public int Height { get; set; } = 500;
}
