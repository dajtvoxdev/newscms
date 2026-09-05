namespace NewsCMS.Domain.Entities.Audit;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = default!;
    public string Entity { get; set; } = default!;
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
}
