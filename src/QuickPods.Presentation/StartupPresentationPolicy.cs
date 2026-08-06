namespace QuickPods.Presentation;

public static class StartupPresentationPolicy
{
    public static bool ShouldShowInitialFlyout(bool trayIconAvailable) =>
        !trayIconAvailable;
}
