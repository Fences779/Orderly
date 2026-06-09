using System.Net;

namespace Orderly.Data.Services;

internal static class OutboundEndpointPolicy
{
    private const string AllowLocalEndpointsEnvironmentVariableName = "ORDERLY_ALLOW_LOCAL_ENDPOINTS";

    public static void Validate(Uri endpoint, string configurationName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (IsRestrictedLocalEndpoint(endpoint) && !IsLocalEndpointAllowedForDevelopment())
        {
            throw new InvalidOperationException($"{configurationName} 不允许指向本机、私网、链路本地或 metadata 地址，除非显式启用本地开发模式。");
        }
    }

    private static bool IsRestrictedLocalEndpoint(Uri endpoint)
    {
        var host = endpoint.Host.Trim('[', ']').Trim().ToLowerInvariant();
        if (host is "localhost" or "0.0.0.0" || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsRestrictedAddress(address);
    }

    private static bool IsRestrictedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.Equals(IPAddress.IPv6Loopback)
                || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }

    private static bool IsLocalEndpointAllowedForDevelopment()
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable(AllowLocalEndpointsEnvironmentVariableName)))
        {
            return false;
        }

        var runtime = (Environment.GetEnvironmentVariable("ORDERLY_RUNTIME_ENV")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? string.Empty).Trim().ToLowerInvariant();

        return runtime is "development" or "dev" or "test" or "local";
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
