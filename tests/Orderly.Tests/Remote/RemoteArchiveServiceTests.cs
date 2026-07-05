using System.Net;
using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;
using Xunit;

namespace Orderly.Tests.Remote;

public sealed class RemoteArchiveServiceTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ListAsync_deserializes_archive_items()
    {
        var workspaceId = Guid.NewGuid();
        var session = RemoteCommerceClientTestExtensions.CreateSession(workspaceId);
        var handler = new FakeHttpMessageHandler().When(
            req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/archive/{EntityType.Order}"),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    Items = new[]
                    {
                        new
                        {
                            Id = Guid.NewGuid(),
                            Name = "Order #1",
                            ArchivedAtUtc = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                            ArchivedByDisplayName = "Admin",
                            ArchiveReason = "Completed",
                            Revision = 7L
                        }
                    }
                }))
            });

        var client = new RemoteCommerceClient("http://localhost:59999", session).WithFakeHandler(handler);
        var service = new RemoteArchiveService(client, session);

        var items = await service.ListAsync(EntityType.Order);

        Assert.Single(items);
        Assert.Equal("Order #1", items[0].Name);
        Assert.Equal(EntityType.Order, items[0].EntityType);
        Assert.Equal(7L, items[0].Revision);
    }

    [Fact]
    public async Task RecoverAsync_posts_recover_command_with_expected_revision()
    {
        var workspaceId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var session = RemoteCommerceClientTestExtensions.CreateSession(workspaceId);
        RecoverCommand? captured = null;
        var handler = new FakeHttpMessageHandler().When(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains($"/archive/{EntityType.Customer}/{entityId:N}/recover"),
            req =>
            {
                var json = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                captured = json is null ? null : JsonSerializer.Deserialize<RecoverCommand>(json, WebOptions);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var client = new RemoteCommerceClient("http://localhost:59999", session).WithFakeHandler(handler);
        var service = new RemoteArchiveService(client, session);

        await service.RecoverAsync(EntityType.Customer, entityId, 12L);

        Assert.NotNull(captured);
        Assert.Equal(12L, captured.ExpectedRevision);
        Assert.False(string.IsNullOrEmpty(captured.ClientRequestId));
    }
}
