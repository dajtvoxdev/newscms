using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaImportJob : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Source { get; set; } = default!;
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int SkippedRows { get; set; }
    public int ErrorRows { get; set; }
    public string? ErrorLog { get; set; }
}
