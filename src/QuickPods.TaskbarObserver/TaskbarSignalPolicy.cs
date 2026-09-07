using QuickPods.Contracts;

namespace QuickPods.TaskbarObserver;

/// <summary>
/// Pure classification of Win32 shell signals into observer invalidation kinds.
/// No signal produced here ever identifies a process or window; the host only
/// learns that the taskbar may have changed and re-runs its own discovery.
/// </summary>
internal static class TaskbarSignalPolicy
{
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan RedrawThrottle = TimeSpan.FromMilliseconds(1000);
    internal static readonly TimeSpan IdentityPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Taskbar labels are hidden unless Explorer's TaskbarGlomLevel is a
    /// non-zero value; a missing value means the default combined layout.
    /// </summary>
    internal static bool AreTaskbarLabelsVisible(object? taskbarGlomLevel) =>
        taskbarGlomLevel is int level && level != 0;

    internal static SignalDisposition ClassifyShellHook(ulong code, bool taskbarLabelsVisible) =>
        code switch
        {
            ObserverNativeMethods.ShellHookWindowCreated or
            ObserverNativeMethods.ShellHookWindowDestroyed or
            ObserverNativeMethods.ShellHookWindowReplaced =>
                new(ObserverInvalidationKind.StructureChanged, ObserverSourceClassification.External, true),
            // Title and icon redraws fire continuously for some applications and
            // only change taskbar geometry while button labels are visible.
            ObserverNativeMethods.ShellHookRedraw when taskbarLabelsVisible =>
                new(ObserverInvalidationKind.StructureChanged, ObserverSourceClassification.External, true),
            _ => SignalDisposition.None,
        };

    internal static bool IsThrottledShellHook(ulong code) =>
        code == ObserverNativeMethods.ShellHookRedraw;

    internal static SignalDisposition ClassifyWinEvent(
        uint eventId,
        int objectId,
        int childId,
        bool isInTaskbarTree)
    {
        if (objectId != ObserverNativeMethods.ObjectIdWindow ||
            childId != ObserverNativeMethods.ChildIdSelf ||
            !isInTaskbarTree)
        {
            return SignalDisposition.None;
        }

        return eventId switch
        {
            ObserverNativeMethods.EventObjectCreate or
            ObserverNativeMethods.EventObjectDestroy or
            ObserverNativeMethods.EventObjectReorder =>
                new(ObserverInvalidationKind.StructureChanged, ObserverSourceClassification.External, true),
            ObserverNativeMethods.EventObjectShow or
            ObserverNativeMethods.EventObjectHide =>
                new(ObserverInvalidationKind.IsOffscreenChanged, ObserverSourceClassification.External, true),
            ObserverNativeMethods.EventObjectLocationChange =>
                new(null, ObserverSourceClassification.Unknown, true),
            _ => SignalDisposition.None,
        };
    }

    internal static SignalDisposition ClassifyWindowMessage(uint message, uint taskbarCreatedMessage)
    {
        if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
        {
            return SettingsChanged;
        }

        return message switch
        {
            ObserverNativeMethods.WmSettingChange or
            ObserverNativeMethods.WmDisplayChange or
            ObserverNativeMethods.WmThemeChanged => SettingsChanged,
            _ => SignalDisposition.None,
        };
    }

    internal static SignalDisposition ClassifyRegistryChange() => SettingsChanged;

    internal static SignalDisposition Settle =>
        new(ObserverInvalidationKind.BoundingRectangleChanged, ObserverSourceClassification.Unknown, false);

    internal static bool ShouldEmitThrottled(
        TimeSpan now,
        TimeSpan? lastEmitted,
        TimeSpan window,
        out bool scheduleTrailing)
    {
        if (lastEmitted is null || now - lastEmitted.Value >= window)
        {
            scheduleTrailing = false;
            return true;
        }

        scheduleTrailing = true;
        return false;
    }

    private static SignalDisposition SettingsChanged =>
        new(ObserverInvalidationKind.StructureChanged, ObserverSourceClassification.Unknown, true);
}

internal readonly record struct SignalDisposition(
    ObserverInvalidationKind? Kind,
    ObserverSourceClassification Source,
    bool ArmSettle)
{
    internal static SignalDisposition None => new(null, ObserverSourceClassification.Unknown, false);

    internal bool IsNone => Kind is null && !ArmSettle;
}
