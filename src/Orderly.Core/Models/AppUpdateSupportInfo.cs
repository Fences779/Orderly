namespace Orderly.Core.Models;

public sealed record AppUpdateSupportInfo(
    bool IsSupported,
    string Channel,
    string SourceDescription,
    string StatusText);
