using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Orderly.Remote.Auth;

[SupportedOSPlatform("windows")]
public sealed class WindowsCloudTokenStorage : ICloudTokenStorage
{
    private readonly string _folder;

    public WindowsCloudTokenStorage(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrderlyData", "CloudTokens");
        Directory.CreateDirectory(_folder);
    }

    public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return File.WriteAllBytesAsync(path, protectedBytes, cancellationToken);
    }

    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
        var protectedBytes = File.ReadAllBytes(path);
        try
        {
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        catch (CryptographicException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string key) => Path.Combine(_folder, $"{Uri.EscapeDataString(key)}.dat");
}
