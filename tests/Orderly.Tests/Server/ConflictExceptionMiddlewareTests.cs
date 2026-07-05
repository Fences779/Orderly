using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server;

public sealed class ConflictExceptionMiddlewareTests
{
    [Fact]
    public async Task Middleware_writes_conflict_payload_with_actor_and_updated_time()
    {
        var updatedAt = new DateTime(2024, 7, 1, 8, 30, 0, DateTimeKind.Utc);
        RequestDelegate next = _ => throw new ConflictException(
            "Price was updated",
            "Alice",
            updatedAt,
            9L);

        var middleware = new ConflictExceptionMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);

        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal("Price was updated", payload.GetProperty("detail").GetString());
        Assert.Equal("Alice", payload.GetProperty("actorDisplayName").GetString());
        Assert.Equal(9L, payload.GetProperty("latestRevision").GetInt64());
        var message = payload.GetProperty("error").GetString();
        Assert.Contains("Alice", message);
        Assert.Contains(updatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), message);
    }

    [Fact]
    public async Task Middleware_writes_conflict_payload_for_idempotency_mismatch()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("同一个 ClientRequestId 被复用但请求内容不一致。");

        var middleware = new ConflictExceptionMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Conflict, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Contains("ClientRequestId", payload.GetProperty("error").GetString());
    }
}
