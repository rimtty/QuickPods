using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Hosting;
using QuickPods.TaskbarHost.Placement;
using QuickPods.TaskbarHost.Presentation;

namespace QuickPods.TaskbarHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        TaskbarHostOptions options = TaskbarHostOptions.Parse(args);
        return options.Command switch
        {
            TaskbarHostCommand.Help => 0,
            TaskbarHostCommand.Inspect => RunInspect(),
            TaskbarHostCommand.Preview => RunPreview(options.PreviewDuration),
            _ => 2,
        };
    }

    private static int RunInspect()
    {
        (TaskbarDiscoveryResult result, TaskbarPlacementResult placement) = DiscoverPlacement();
        TaskbarPresentationRoute route = TaskbarPresentationRouter.Select(result, placement);
        WriteSanitizedReport(result, placement, route);
        return ExitCodeFor(placement, route);
    }

    private static int RunPreview(TimeSpan duration)
    {
        (TaskbarDiscoveryResult result, TaskbarPlacementResult placement) = DiscoverPlacement();
        TaskbarPresentationRoute route = TaskbarPresentationRouter.Select(result, placement);
        WriteSanitizedReport(result, placement, route);
        if (route.Surface == TaskbarPresentationSurface.Hidden ||
            route.Bounds is not { } bounds ||
            result.Snapshot is not LiveTaskbarSnapshot snapshot)
        {
            return ExitCodeFor(placement, route);
        }

        try
        {
            if (route.Surface == TaskbarPresentationSurface.Native)
            {
                RunNativePreview(snapshot, bounds, duration);
            }
            else if (route.VerifiedWorkArea is PixelRect workArea)
            {
                RunFloatingPreview(bounds, workArea, route.Dpi, duration);
            }
            else
            {
                return 4;
            }

            Console.WriteLine("preview_shutdown=natural");
            return 0;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"preview_failure={exception.GetType().Name}");
            return 5;
        }
    }

    private static (TaskbarDiscoveryResult Discovery, TaskbarPlacementResult Placement) DiscoverPlacement()
    {
        TaskbarDiscoveryResult result = TaskbarDiscoveryService.DiscoverAsync().GetAwaiter().GetResult();
        TaskbarLayoutObservation? observation = TaskbarObservationAdapter.Create(result);
        TaskbarPlacementResult placement = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        return (result, placement);
    }

    private static void WriteSanitizedReport(
        TaskbarDiscoveryResult result,
        TaskbarPlacementResult placement,
        TaskbarPresentationRoute route)
    {
        Console.WriteLine($"protocol={QuickPodsProtocol.Version}");
        Console.WriteLine($"discovery_complete={result.IsComplete.ToString().ToLowerInvariant()}");
        Console.WriteLine($"faults={string.Join(',', result.Faults.Order())}");
        Console.WriteLine($"decision={placement.Decision}");
        Console.WriteLine($"reason={placement.Reason}");
        Console.WriteLine($"surface={route.Surface}");
        Console.WriteLine($"surface_reason={route.Reason}");
        if (result.Snapshot is LiveTaskbarSnapshot snapshot)
        {
            Console.WriteLine($"dpi={snapshot.Dpi}");
            Console.WriteLine($"taskbar_size={snapshot.Bounds.Width}x{snapshot.Bounds.Height}");
            Console.WriteLine($"automation_buttons={snapshot.AutomationButtons.Count}");
            Console.WriteLine($"native_obstacles={snapshot.NativeObstacles.Count}");
        }
    }

    private static void RunNativePreview(
        LiveTaskbarSnapshot snapshot,
        PixelRect bounds,
        TimeSpan duration)
    {
        using var host = new NativeTaskbarHost();
        AttachDiagnostics(host);
        host.Create(
            snapshot.TaskbarHandle,
            bounds,
            snapshot.Dpi,
            new TaskbarStateSnapshot(TaskbarSurfaceMode.Native, 42, false, null));
        PumpFor(duration, host.PumpMessages);
        host.Destroy();
    }

    private static void RunFloatingPreview(
        PixelRect bounds,
        PixelRect workArea,
        uint dpi,
        TimeSpan duration)
    {
        using var host = new NativeFloatingHost();
        AttachDiagnostics(host);
        host.Create(
            bounds,
            workArea,
            dpi,
            new TaskbarStateSnapshot(TaskbarSurfaceMode.Floating, 42, false, null));
        PumpFor(duration, host.PumpMessages);
        host.Destroy();
    }

    private static void PumpFor(TimeSpan duration, Func<int> pumpMessages)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            _ = pumpMessages();
            Thread.Sleep(8);
        }
    }

    private static void AttachDiagnostics(NativeTaskbarHost host)
    {
        host.Interaction += WriteInteraction;
        host.LayoutInvalidated += WriteInvalidation;
    }

    private static void AttachDiagnostics(NativeFloatingHost host)
    {
        host.Interaction += WriteInteraction;
        host.LayoutInvalidated += WriteInvalidation;
    }

    private static void WriteInteraction(HostInteractionEnvelope interaction) =>
        Console.WriteLine(
            $"interaction={interaction.Kind};sequence={interaction.Sequence};volume={interaction.VolumePercent}");

    private static void WriteInvalidation(NativeLayoutInvalidationReason reason) =>
        Console.WriteLine($"layout_invalidated={reason}");

    private static int ExitCodeFor(
        TaskbarPlacementResult placement,
        TaskbarPresentationRoute route) =>
        (placement.Decision, route.Surface) switch
        {
            (PlacementDecision.Place, TaskbarPresentationSurface.Native) => 0,
            (PlacementDecision.VerifiedNoFit, _) => 3,
            _ => 4,
        };
}
