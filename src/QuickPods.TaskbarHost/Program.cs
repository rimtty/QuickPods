using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Placement;

namespace QuickPods.TaskbarHost;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            return 0;
        }

        if (args is not ["inspect"])
        {
            return 2;
        }

        TaskbarDiscoveryResult result = await TaskbarDiscoveryService.DiscoverAsync().ConfigureAwait(false);
        TaskbarLayoutObservation? observation = TaskbarObservationAdapter.Create(result);
        TaskbarPlacementResult placement = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        Console.WriteLine($"protocol={QuickPodsProtocol.Version}");
        Console.WriteLine($"discovery_complete={result.IsComplete.ToString().ToLowerInvariant()}");
        Console.WriteLine($"faults={string.Join(',', result.Faults.Order())}");
        Console.WriteLine($"decision={placement.Decision}");
        Console.WriteLine($"reason={placement.Reason}");
        if (result.Snapshot is LiveTaskbarSnapshot snapshot)
        {
            Console.WriteLine($"dpi={snapshot.Dpi}");
            Console.WriteLine($"taskbar_size={snapshot.Bounds.Width}x{snapshot.Bounds.Height}");
            Console.WriteLine($"automation_buttons={snapshot.AutomationButtons.Count}");
            Console.WriteLine($"native_obstacles={snapshot.NativeObstacles.Count}");
        }

        return placement.Decision switch
        {
            PlacementDecision.Place => 0,
            PlacementDecision.VerifiedNoFit => 3,
            _ => 4,
        };
    }
}
