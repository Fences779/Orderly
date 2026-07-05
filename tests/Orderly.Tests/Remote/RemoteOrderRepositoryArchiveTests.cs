using System.Net;
using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Remote.Clients;
using Orderly.Remote.Repositories;
using Xunit;

namespace Orderly.Tests.Remote;

public sealed class RemoteOrderRepositoryArchiveTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DeleteAsync_archives_order_with_reason()
    {
        var workspaceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var session = RemoteCommerceClientTestExtensions.CreateSession(workspaceId);
        ArchiveCommand? captured = null;

        var handler = new FakeHttpMessageHandler()
            .When(
                req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/orders/{orderId:N}"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new CloudOrderDto
                    {
                        Id = orderId,
                        Revision = 5L
                    }))
                })
            .When(
                req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains($"/archive/{EntityType.Order}/{orderId:N}"),
                req =>
                {
                    var json = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    captured = json is null ? null : JsonSerializer.Deserialize<ArchiveCommand>(json, WebOptions);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });

        var client = new RemoteCommerceClient("http://localhost:59999", session).WithFakeHandler(handler);
        var repository = new RemoteOrderRepository(client, session);

        await repository.DeleteAsync(orderId, "客户取消");

        Assert.NotNull(captured);
        Assert.Equal(orderId, captured.EntityId);
        Assert.Equal(EntityType.Order, captured.EntityType);
        Assert.Equal(5L, captured.ExpectedRevision);
        Assert.Equal("客户取消", captured.ArchiveReason);
    }

    [Fact]
    public async Task DeleteAsync_without_reason_uses_default_reason()
    {
        var workspaceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var session = RemoteCommerceClientTestExtensions.CreateSession(workspaceId);
        ArchiveCommand? captured = null;

        var handler = new FakeHttpMessageHandler()
            .When(
                req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/orders/{orderId:N}"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new CloudOrderDto
                    {
                        Id = orderId,
                        Revision = 1L
                    }))
                })
            .When(
                req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains($"/archive/{EntityType.Order}/{orderId:N}"),
                req =>
                {
                    var json = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    captured = json is null ? null : JsonSerializer.Deserialize<ArchiveCommand>(json, WebOptions);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });

        var client = new RemoteCommerceClient("http://localhost:59999", session).WithFakeHandler(handler);
        var repository = new RemoteOrderRepository(client, session);

        await repository.DeleteAsync(orderId);

        Assert.NotNull(captured);
        Assert.Equal("Remote soft delete", captured.ArchiveReason);
    }
}
