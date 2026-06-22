namespace Orderly.Core.Services;

public interface IWindowsHelloService
{
    Task<bool> IsAvailableAsync();
    Task<bool> VerifyAsync(string message);
}
