using System.Net;

namespace Orderly.Server.Services;

public sealed class ConflictExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ConflictExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ConflictException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "数据已被他人修改，你的修改没有覆盖对方内容。请刷新后重新确认。",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("同一个 ClientRequestId"))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "同一个 ClientRequestId 被错误复用。",
                Detail = ex.Message
            });
        }
    }
}

public static class ConflictExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseConflictExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ConflictExceptionMiddleware>();
    }
}
