using System.Reflection;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Tests.Remote;

/// <summary>
/// 测试辅助：把 RemoteCommerceClient 内部的 HttpClient 替换为使用 FakeHttpMessageHandler 的实例，
/// 使 Remote 服务可以在不监听真实端口的情况下被单元测试。
/// </summary>
public static class RemoteCommerceClientTestExtensions
{
    private static readonly FieldInfo HttpClientField = typeof(RemoteCommerceClient).GetField(
        "_httpClient",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static RemoteCommerceClient WithFakeHandler(this RemoteCommerceClient client, FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:59999/")
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        HttpClientField.SetValue(client, httpClient);
        return client;
    }

    public static CloudAuthSession CreateSession(Guid? workspaceId = null, Guid? userId = null, string role = "Admin")
    {
        return new CloudAuthSession
        {
            AccessToken = "test-token",
            WorkspaceMembership = new Orderly.Contracts.Auth.CloudWorkspaceMembershipDto
            {
                WorkspaceId = workspaceId ?? Guid.NewGuid(),
                CloudRole = role
            },
            User = new Orderly.Contracts.Auth.CloudUserDto
            {
                Id = userId ?? Guid.NewGuid(),
                DisplayName = "Test User"
            }
        };
    }
}
