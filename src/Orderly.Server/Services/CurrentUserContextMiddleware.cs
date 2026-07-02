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

            if (Guid.TryParse(userId, out var parsedUserId) && int.TryParse(tokenVersionValue, out var tokenVersion))
            {
                currentUser.Set(parsedUserId, username ?? string.Empty, displayName ?? string.Empty, tokenVersion);
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
