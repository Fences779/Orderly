namespace Orderly.Core.Models;

public sealed record AppUpdateDownloadResult(
    bool IsSuccess,
    string StatusText,
    string? TargetVersion = null);
