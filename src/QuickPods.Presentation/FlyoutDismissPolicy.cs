namespace QuickPods.Presentation;

public enum FlyoutDismissAction
{
    Ignore,
    Rearm,
    Hide,
}

public static class FlyoutDismissPolicy
{
    public static FlyoutDismissAction Decide(bool previewActive, bool pointerWithinFlyout) =>
        !previewActive
            ? FlyoutDismissAction.Ignore
            : pointerWithinFlyout
                ? FlyoutDismissAction.Rearm
                : FlyoutDismissAction.Hide;
}
