namespace Orderly.Core.Models;

public sealed record AppUpdateCheckResult(
    AppUpdateState State,
    string StatusText,
    string CurrentVersion,
    string? AvailableVersion = null,
    string? ReleaseNotesMarkdown = null);
