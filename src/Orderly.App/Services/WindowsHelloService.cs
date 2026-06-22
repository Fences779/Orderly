using Orderly.Core.Services;
using Windows.Security.Credentials.UI;

namespace Orderly.App.Services;

public sealed class WindowsHelloService : IWindowsHelloService
{
    public async Task<bool> IsAvailableAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return false;
        }

        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> VerifyAsync(string message)
    {
        if (!await IsAvailableAsync())
        {
            return false;
        }

        try
        {
            return await UserConsentVerifier.RequestVerificationAsync(message)
                == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }
}
