namespace Orderly.Server.Services;

public sealed class CurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; private set; }
    public string? Username { get; private set; }
    public string? DisplayName { get; private set; }
    public int TokenVersion { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public void Set(Guid userId, string username, string displayName, int tokenVersion)
    {
        UserId = userId;
        Username = username;
        DisplayName = displayName;
        TokenVersion = tokenVersion;
    }

    public void Clear()
    {
        UserId = null;
        Username = null;
        DisplayName = null;
        TokenVersion = 0;
    }
}
