using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Ai;

public class AiConnection : AuditableEntity, ISoftDelete
{
    public string Name { get; set; } = default!;
    public string Provider { get; set; } = default!;
    public string BaseUrl { get; set; } = default!;
    public string ApiKeyEncrypted { get; set; } = default!;
    public string DefaultModel { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public string? Description { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
