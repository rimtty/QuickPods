namespace QuickPods.App;

internal enum ApplicationUpdateViewStatus
{
    NotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    Failed,
}

internal sealed record ApplicationUpdateViewState(
    ApplicationUpdateViewStatus Status,
    DateTimeOffset? CheckedAtUtc = null,
    Version? LatestVersion = null,
    Uri? ReleasePage = null,
    string? ErrorDetail = null)
{
    internal static ApplicationUpdateViewState NotChecked { get; } = new(
        ApplicationUpdateViewStatus.NotChecked);

    internal static ApplicationUpdateViewState Checking { get; } = new(
        ApplicationUpdateViewStatus.Checking);
}

internal enum ApplicationUpdateCheckTrigger
{
    Tray,
    Settings,
    Automatic,
}
