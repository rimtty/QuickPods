using QuickPods.Contracts;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Hosting;

internal readonly record struct TaskbarRenderState
{
    private TaskbarRenderState(double volumeFraction, bool isMuted)
    {
        VolumeFraction = volumeFraction;
        IsMuted = isMuted;
    }

    internal double VolumeFraction { get; }

    internal bool IsMuted { get; }

    internal static TaskbarRenderState FromSnapshot(TaskbarStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        double volumeFraction = Math.Clamp(snapshot.VolumePercent, 0, 100) / 100d;
        return new(volumeFraction, snapshot.IsMuted);
    }
}

internal readonly record struct TaskbarRenderTheme(
    uint TransparentColorKey,
    uint SurfaceColor,
    uint SurfaceOutlineColor,
    uint TrackColor,
    uint AccentColor,
    uint ForegroundColor)
{
    // GDI COLORREF stores bytes as 0x00BBGGRR.
    internal static TaskbarRenderTheme Dark { get; } = new(
        0x00FF00FF,
        0x0024211F,
        0x0045413E,
        0x00524D49,
        0x00EED95E,
        0x00F4F2F0);

    internal static TaskbarRenderTheme Current
    {
        get
        {
            var highContrast = new HostNativeMethods.NativeHighContrast
            {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<
                    HostNativeMethods.NativeHighContrast>(),
            };
            if (!HostNativeMethods.SystemParametersInfo(
                    HostNativeMethods.SpiGetHighContrast,
                    highContrast.Size,
                    ref highContrast,
                    0) ||
                (highContrast.Flags & HostNativeMethods.HighContrastOn) == 0)
            {
                return Dark;
            }

            return new TaskbarRenderTheme(
                Dark.TransparentColorKey,
                HostNativeMethods.GetSystemColor(HostNativeMethods.ColorWindow),
                HostNativeMethods.GetSystemColor(HostNativeMethods.ColorWindowText),
                HostNativeMethods.GetSystemColor(HostNativeMethods.ColorGrayText),
                HostNativeMethods.GetSystemColor(HostNativeMethods.ColorHighlight),
                HostNativeMethods.GetSystemColor(HostNativeMethods.ColorWindowText));
        }
    }
}
