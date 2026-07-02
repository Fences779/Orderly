namespace Orderly.Server.Services;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? Username { get; }
    string? DisplayName { get; }
    int TokenVersion { get; }
    bool IsAuthenticated { get; }
    void Set(Guid userId, string username, string displayName, int tokenVersion);
    void Clear();
}
