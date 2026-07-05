using System.Net;
using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;
using Xunit;

namespace Orderly.Tests.Remote;

public sealed class RemotePriceChangeRequestServiceTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SubmitAsync_posts_price_change_request_command()
    {
        var workspaceId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var session = RemoteCommerceClientTestExtensions.CreateSession(workspaceId);
        PriceChangeRequestCommand? captured = null;

        var handler = new FakeHttpMessageHandler().When(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/price-change-requests"),
            req =>
            {
                var json = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                captured = json is null ? null : JsonSerializer.Deserialize<PriceChangeRequestCommand>(json, WebOptions);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var client = new RemoteCommerceClient("http://localhost:59999", session).WithFakeHandler(handler);
        var service = new RemotePriceChangeRequestService(client, session);

        await service.SubmitAsync(productId, 199.99m, "促销改价");

        Assert.NotNull(captured);
        Assert.Equal(productId, captured.ProductId);
        Assert.Equal(199.99m, captured.ProposedPrice);
        Assert.Equal("促销改价", captured.ChangeReason);
        Assert.False(string.IsNullOrEmpty(captured.ClientRequestId));
    }
}
