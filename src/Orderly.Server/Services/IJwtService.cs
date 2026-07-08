using System.Security.Claims;

namespace Orderly.Server.Services;

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string username, string displayName, int tokenVersion, string deviceId);
    ClaimsPrincipal? ValidateAccessToken(string token);
}
