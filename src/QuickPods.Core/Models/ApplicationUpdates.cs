namespace QuickPods.Core.Models;

public sealed record ApplicationUpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    Uri ReleasePage,
    DateTimeOffset CheckedAtUtc)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}
