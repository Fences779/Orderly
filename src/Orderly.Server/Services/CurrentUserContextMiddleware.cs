using System.Security.Claims;

namespace Orderly.Server.Services;

public sealed class CurrentUserContextMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserContext currentUser)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
            var displayName = context.User.FindFirstValue("display_name");
            var username = context.User.FindFirstValue(ClaimTypes.Name)
                        ?? context.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName);
            var tokenVersionValue = context.User.FindFirstValue("token_version");
            var role = context.User.FindFirstValue(ClaimTypes.Role);
            var businessLabel = context.User.FindFirstValue("business_label");
            var workspaceIdValue = context.User.FindFirstValue("workspace_id");

            if (Guid.TryParse(userId, out var parsedUserId)
                && Guid.TryParse(workspaceIdValue, out var parsedWorkspaceId)
                && int.TryParse(tokenVersionValue, out var tokenVersion))
            {
                currentUser.Set(
                    parsedUserId,
                    username ?? string.Empty,
                    displayName ?? string.Empty,
                    role ?? string.Empty,
                    businessLabel ?? string.Empty,
                    parsedWorkspaceId,
                    tokenVersion);
            }
        }

        await _next(context);
    }
}

public static class CurrentUserContextMiddlewareExtensions
{
    public static IApplicationBuilder UseCurrentUserContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CurrentUserContextMiddleware>();
    }
}
