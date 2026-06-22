using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IQuickLoginService
{
    Task<QuickLoginStatus> GetStatusAsync(string username, CancellationToken cancellationToken = default);
    Task SetEnabledForCurrentAccountAsync(bool enabled, CancellationToken cancellationToken = default);
    Task CaptureCurrentPasswordSessionAsync(string username, bool enableQuickLogin, CancellationToken cancellationToken = default);
    Task<LocalSignInResult> SignInWithPinAsync(string username, string pin, CancellationToken cancellationToken = default);
    Task<LocalSignInResult> SignInWithWindowsHelloAsync(string username, CancellationToken cancellationToken = default);
}
