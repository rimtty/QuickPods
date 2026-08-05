using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal static class NativeDpiAwarenessProbe
{
    private static readonly nint PerMonitorV2Context = new(-4);

    internal static bool IsStableAfterParenting(
        NativeDpiAwarenessMeasurement before,
        NativeDpiAwarenessMeasurement after,
        uint parentDpi)
    {
        if (parentDpi == 0 ||
            before.WindowDpi == 0 ||
            before.Process == NativeDpiAwareness.Unknown ||
            before.Thread == NativeDpiAwareness.Unknown ||
            before.Window == NativeDpiAwareness.Unknown ||
            after.Process == NativeDpiAwareness.Unknown ||
            after.Thread == NativeDpiAwareness.Unknown ||
            after.Window == NativeDpiAwareness.Unknown)
        {
            return false;
        }

        return after.Process == before.Process &&
            after.Thread == before.Thread &&
            after.Window == before.Window &&
            after.ThreadIsPerMonitorV2 == before.ThreadIsPerMonitorV2 &&
            after.WindowIsPerMonitorV2 == before.WindowIsPerMonitorV2 &&
            after.WindowDpi == parentDpi;
    }

    internal static bool IsPerMonitorV2StableAfterParenting(
        NativeDpiAwarenessMeasurement before,
        NativeDpiAwarenessMeasurement after,
        uint parentDpi)
    {
        return IsStableAfterParenting(before, after, parentDpi) &&
            before.Process == NativeDpiAwareness.PerMonitorAware &&
            before.Thread == NativeDpiAwareness.PerMonitorAware &&
            before.Window == NativeDpiAwareness.PerMonitorAware &&
            before.ThreadIsPerMonitorV2 &&
            before.WindowIsPerMonitorV2 &&
            before.WindowDpi == parentDpi &&
            after.Process == NativeDpiAwareness.PerMonitorAware &&
            after.Thread == NativeDpiAwareness.PerMonitorAware &&
            after.Window == NativeDpiAwareness.PerMonitorAware &&
            after.ThreadIsPerMonitorV2 &&
            after.WindowIsPerMonitorV2;
    }

    internal static NativeDpiAwarenessMeasurement Measure(nint window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(
                NativeDpiAwareness.Unknown,
                NativeDpiAwareness.Unknown,
                NativeDpiAwareness.Unknown,
                false,
                false,
                0);
        }

        int processValue = -1;
        try
        {
            if (NativeMethods.GetProcessDpiAwareness(nint.Zero, out int measuredProcessValue) >= 0)
            {
                processValue = measuredProcessValue;
            }
        }
        catch (DllNotFoundException)
        {
            // The process measurement remains Unknown on an unsupported platform.
        }
        catch (EntryPointNotFoundException)
        {
            // The process measurement remains Unknown on an unsupported Windows build.
        }

        nint threadContext = NativeMethods.GetThreadDpiAwarenessContext();
        nint windowContext = window == nint.Zero
            ? nint.Zero
            : NativeMethods.GetWindowDpiAwarenessContext(window);
        NativeDpiAwareness thread = MeasureContext(threadContext);
        NativeDpiAwareness windowAwareness = window == nint.Zero
            ? NativeDpiAwareness.Unknown
            : MeasureContext(windowContext);
        uint windowDpi = window == nint.Zero ? 0 : NativeMethods.GetDpiForWindow(window);

        return new(
            Convert(processValue),
            thread,
            windowAwareness,
            IsPerMonitorV2(threadContext),
            IsPerMonitorV2(windowContext),
            windowDpi);
    }

    private static bool IsPerMonitorV2(nint context) =>
        context != nint.Zero &&
        NativeMethods.AreDpiAwarenessContextsEqual(context, PerMonitorV2Context);

    private static NativeDpiAwareness MeasureContext(nint context) =>
        context == nint.Zero
            ? NativeDpiAwareness.Unknown
            : Convert(NativeMethods.GetAwarenessFromDpiAwarenessContext(context));

    private static NativeDpiAwareness Convert(int value) => value switch
    {
        0 => NativeDpiAwareness.Unaware,
        1 => NativeDpiAwareness.SystemAware,
        2 => NativeDpiAwareness.PerMonitorAware,
        _ => NativeDpiAwareness.Unknown,
    };
}
