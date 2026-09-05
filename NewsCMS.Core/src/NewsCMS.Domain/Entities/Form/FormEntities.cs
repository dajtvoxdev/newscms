using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Form;

public class ContactForm : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Code { get; set; } = default!;     // identifier dùng trong theme
    public string Name { get; set; } = default!;
    public string FieldsJson { get; set; } = "[]";   // schema field định nghĩa dạng JSON
    public string? NotifyEmails { get; set; }        // danh sách email cách nhau bởi ;
    public string? SuccessMessage { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<FormSubmission> Submissions { get; set; } = new List<FormSubmission>();
}

public class FormSubmission : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid FormId { get; set; }
    public ContactForm Form { get; set; } = default!;
    public string DataJson { get; set; } = "{}";
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
}
