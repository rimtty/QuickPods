using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

namespace QuickPods.Spike.TaskbarHost.Discovery;

/// <summary>
/// Performs a read-only scan of the primary Windows taskbar. The service never
/// guesses: any missing, duplicate, failed or timed-out observation makes the
/// result incomplete.
/// </summary>
internal sealed class TaskbarDiscoveryService
{
    private readonly nint ignoredWindowHandle;

    internal TaskbarDiscoveryService(nint ignoredWindowHandle = default)
    {
        this.ignoredWindowHandle = ignoredWindowHandle;
    }

    /// <summary>
    /// Enumerates the primary taskbar, its critical Win32 children and visible
    /// UIA buttons. UI Automation is bounded to five seconds on a background MTA.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This read-only process boundary must fail closed instead of crashing the host on an unexpected OS failure.")]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance boundary is retained as the replacement seam for watchdog-era discovery dependencies.")]
    internal async Task<TaskbarDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new TaskbarDiscoveryResult(
                null,
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.UnsupportedPlatform)]);
        }

        try
        {
            return await DiscoverOnWindowsAsync(
                ignoredWindowHandle,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateUnexpectedFailure(ignoredHostMatch: false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<TaskbarDiscoveryResult> DiscoverOnWindowsAsync(
        nint ignoredWindowHandle,
        CancellationToken cancellationToken)
    {
        Win32TaskbarProbe win32 = Win32TaskbarDiscovery.Discover(ignoredWindowHandle);
        try
        {
            if (win32.Target is null)
            {
                return new TaskbarDiscoveryResult(
                    null,
                    win32.Faults,
                    win32.IgnoredHostMatch);
            }

            AutomationTaskbarProbe automation = await TaskbarAutomationDiscovery.DiscoverAsync(
                win32.Target.TaskbarHandle,
                win32.Target.Bounds,
                cancellationToken).ConfigureAwait(false);

            TaskbarSnapshot snapshot = new(
                win32.Target.TaskbarHandle,
                win32.Target.ExplorerProcessId,
                win32.Target.Bounds,
                win32.Target.Dpi,
                win32.Target.Monitor,
                win32.Target.CriticalChildren,
                automation.Buttons);
            return new TaskbarDiscoveryResult(
                snapshot,
                win32.Faults.Concat(automation.Faults),
                win32.IgnoredHostMatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateUnexpectedFailure(win32.IgnoredHostMatch);
        }
    }

    internal static TaskbarDiscoveryResult CreateUnexpectedFailure(bool ignoredHostMatch) =>
        new(
            null,
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.UnexpectedDiscoveryFailure)],
            ignoredHostMatch);
}
