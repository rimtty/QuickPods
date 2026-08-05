using System.Diagnostics;
using System.IO;
using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int InvalidArgumentsExitCode = 2;
    private const int ObservationUnavailableExitCode = 3;
    private const int HostFailureExitCode = 4;
    private const int CanceledExitCode = 5;

    public static async Task<int> Main(string[] args)
    {
        OptionsParseResult parseResult = TaskbarHostOptions.Parse(args);
        if (!parseResult.IsSuccess)
        {
            Console.Error.WriteLine(parseResult.Error);
            WriteUsage(Console.Error);
            return InvalidArgumentsExitCode;
        }

        TaskbarHostOptions options = parseResult.Options!;
        if (options.Command == TaskbarCommand.Help)
        {
            WriteUsage(Console.Out);
            return SuccessExitCode;
        }

        using var cancellation = new CancellationTokenSource();
        void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await RunAsync(options, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("The diagnostic was canceled; any created host window was destroyed.");
            return CanceledExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The diagnostic failed closed ({exception.GetType().Name}).");
            return HostFailureExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<int> RunAsync(
        TaskbarHostOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var discoveryService = new TaskbarDiscoveryService();
        TaskbarDiscoveryResult discovery = await discoveryService
            .DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        TaskbarLayoutObservation? observation = TaskbarLayoutAdapter.CreateObservation(discovery);
        TaskbarPlacementResult placement = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        var report = SanitizedTaskbarReport.Create(
            discovery,
            placement,
            stopwatch.Elapsed);
        Console.Out.WriteLine(report.ToJson());

        if (options.Command == TaskbarCommand.Inspect)
        {
            return placement.Decision == PlacementDecision.TransientUnknown
                ? ObservationUnavailableExitCode
                : SuccessExitCode;
        }

        bool completed = await TaskbarHostRunner.RunAsync(options, cancellationToken).ConfigureAwait(false);
        return completed ? SuccessExitCode : HostFailureExitCode;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("QuickPods Taskbar Host Phase 0D diagnostic");
        writer.WriteLine();
        writer.WriteLine("  inspect");
        writer.WriteLine("      Read-only taskbar/UI Automation discovery and placement report.");
        writer.WriteLine();
        writer.WriteLine(
            "  host [--style child|popup] [--fallback floating|hidden] " +
            "[--duration 1..120] --confirm-live-host");
        writer.WriteLine(
            "      Temporarily show the sample host. Native placement is used only after verification;");
        writer.WriteLine(
            "      otherwise the selected safe fallback remains active for the bounded run.");
        writer.WriteLine();
        writer.WriteLine("The spike never restarts Explorer or changes audio/Bluetooth state.");
    }
}
