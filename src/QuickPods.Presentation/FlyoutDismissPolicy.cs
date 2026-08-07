namespace QuickPods.Presentation;

public enum FlyoutDismissAction
{
    Ignore,
    Rearm,
    Hide,
}

public static class FlyoutDismissPolicy
{
    public static FlyoutDismissAction Decide(bool autoDismissActive, bool pointerWithinFlyout) =>
        !autoDismissActive
            ? FlyoutDismissAction.Ignore
            : pointerWithinFlyout
                ? FlyoutDismissAction.Rearm
                : FlyoutDismissAction.Hide;
}
