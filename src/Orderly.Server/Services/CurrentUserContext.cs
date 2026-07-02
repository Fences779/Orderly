namespace Orderly.Server.Services;

public sealed class CurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; private set; }
    public string? Username { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Role { get; private set; }
    public string? BusinessLabel { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public int TokenVersion { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public void Set(Guid userId, string username, string displayName, string role, string businessLabel, Guid workspaceId, int tokenVersion)
    {
        UserId = userId;
        Username = username;
        DisplayName = displayName;
        Role = role;
        BusinessLabel = businessLabel;
        WorkspaceId = workspaceId;
        TokenVersion = tokenVersion;
    }

    public void Clear()
    {
        UserId = null;
        Username = null;
        DisplayName = null;
        Role = null;
        BusinessLabel = null;
        WorkspaceId = null;
        TokenVersion = 0;
    }
}
