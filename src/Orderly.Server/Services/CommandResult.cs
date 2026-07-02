namespace Orderly.Server.Services;

public sealed class CommandResult<T>
{
    public T Value { get; }
    public bool IsReplay { get; }

    public CommandResult(T value, bool isReplay)
    {
        Value = value;
        IsReplay = isReplay;
    }
}
