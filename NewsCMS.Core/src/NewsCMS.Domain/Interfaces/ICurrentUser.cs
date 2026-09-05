namespace NewsCMS.Domain.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Permissions { get; }
    bool HasPermission(string code);
}

public interface IDateTime
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
}
