using System.Net;

namespace Orderly.Data.Services;

internal static class OutboundEndpointPolicy
{
    private const string AllowLocalEndpointsEnvironmentVariableName = "ORDERLY_ALLOW_LOCAL_ENDPOINTS";
    private const string AllowedOutboundHostsEnvironmentVariableName = "ORDERLY_ALLOWED_OUTBOUND_HOSTS";

    public static void Validate(Uri endpoint, string configurationName, string? allowedHostsEnvironmentVariableName = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        ValidateUriSurface(endpoint, configurationName);

        if (IsRestrictedLocalEndpoint(endpoint) && !IsLocalEndpointAllowedForDevelopment())
        {
            throw new InvalidOperationException($"{configurationName} 不允许指向本机、私网、链路本地或 metadata 地址，除非显式启用本地开发模式。");
        }

        var allowedHosts = ReadAllowedHosts(allowedHostsEnvironmentVariableName);
        if (allowedHosts.Count > 0 && !IsHostAllowed(endpoint.Host, allowedHosts))
        {
            throw new InvalidOperationException($"{configurationName} 的主机不在允许的出站主机列表内。");
        }

        ValidateResolvedAddresses(endpoint, configurationName);
    }

    private static void ValidateUriSurface(Uri endpoint, string configurationName)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            throw new InvalidOperationException($"{configurationName} 不允许在 URL 中包含用户名、密码或其他 userinfo。");
        }

        if (!string.IsNullOrWhiteSpace(endpoint.Fragment))
        {
            throw new InvalidOperationException($"{configurationName} 不允许包含 URL fragment。");
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

    private static void ValidateResolvedAddresses(Uri endpoint, string configurationName)
    {
        if (IsLocalEndpointAllowedForDevelopment())
        {
            return;
        }

        var host = endpoint.DnsSafeHost.Trim('[', ']').Trim();
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _))
        {
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            throw new InvalidOperationException($"{configurationName} 的主机无法解析。", ex);
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"{configurationName} 的主机无法解析。");
        }

        if (addresses.Any(IsRestrictedAddress))
        {
            throw new InvalidOperationException($"{configurationName} 解析到了本机、私网、链路本地或 metadata 地址，已拒绝出站请求。");
        }
    }

    private static bool IsRestrictedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsRestrictedAddress(address.MapToIPv4());
        }

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

    private static IReadOnlyList<string> ReadAllowedHosts(string? scopedEnvironmentVariableName)
    {
        var values = new List<string>();
        AddAllowedHosts(values, Environment.GetEnvironmentVariable(AllowedOutboundHostsEnvironmentVariableName));
        if (!string.IsNullOrWhiteSpace(scopedEnvironmentVariableName))
        {
            AddAllowedHosts(values, Environment.GetEnvironmentVariable(scopedEnvironmentVariableName));
        }

        return values
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddAllowedHosts(ICollection<string> values, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        foreach (var item in rawValue.Split([',', ';', ' ', '\t', '\r', '\n', '，', '；'], StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(item);
        }
    }

    private static bool IsHostAllowed(string host, IReadOnlyList<string> allowedHosts)
    {
        var normalizedHost = host.Trim('[', ']').Trim().ToLowerInvariant();
        return allowedHosts.Any(pattern =>
            string.Equals(normalizedHost, pattern, StringComparison.Ordinal)
            || (pattern.StartsWith("*.", StringComparison.Ordinal)
                && normalizedHost.EndsWith(pattern[1..], StringComparison.Ordinal)
                && normalizedHost.Length > pattern.Length - 1));
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
