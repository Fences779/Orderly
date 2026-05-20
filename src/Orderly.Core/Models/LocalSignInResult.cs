namespace Orderly.Core.Models;

public sealed class LocalSignInResult
{
    public bool Succeeded { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public LocalSessionContext? Session { get; init; }

    public static LocalSignInResult Success(LocalSessionContext session)
    {
        return new LocalSignInResult
        {
            Succeeded = true,
            Session = session
        };
    }

    public static LocalSignInResult Failure(string message)
    {
        return new LocalSignInResult
        {
            Succeeded = false,
            ErrorMessage = message
        };
    }
}
