namespace Orderly.Server.Services;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? Username { get; }
    string? DisplayName { get; }
    string? Role { get; }
    string? BusinessLabel { get; }
    Guid? WorkspaceId { get; }
    string? DeviceId { get; }
    int TokenVersion { get; }
    bool IsAuthenticated { get; }
    void Set(Guid userId, string username, string displayName, string role, string businessLabel, Guid workspaceId, string deviceId, int tokenVersion);
    void Clear();
}
