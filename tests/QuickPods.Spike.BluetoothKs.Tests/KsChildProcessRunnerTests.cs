using System.Diagnostics;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class KsChildProcessRunnerTests
{
    [Fact]
    public async Task WatchdogTerminatesOnlyTheHungChildWithinItsBound()
    {
        var runner = new KsChildProcessRunner(
            TimeSpan.FromMilliseconds(100),
            GetSpikeExecutablePath());
        var stopwatch = Stopwatch.StartNew();

        KsChildRunResult result = await runner.RunAsync(
            new KsChildInvocation(
                KsChildProtocol.SimulationTarget,
                "non-device-test-input",
                KsChildOperation.SimulatedHang,
                KsChildConsent.Simulation),
            CancellationToken.None);

        Assert.Equal(KsChildRunStatus.TimedOut, result.Status);
        Assert.Null(result.Response);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.NotNull(result.ProcessId);
        Assert.False(IsProcessRunning(result.ProcessId.Value));
    }

    [Fact]
    public async Task AbnormalChildExitIsNeverReportedAsCompleted()
    {
        var runner = new KsChildProcessRunner(
            TimeSpan.FromSeconds(1),
            GetSpikeExecutablePath());

        KsChildRunResult result = await runner.RunAsync(
            new KsChildInvocation(
                KsChildProtocol.SimulationTarget,
                "non-device-test-input",
                KsChildOperation.SimulatedFault,
                KsChildConsent.Simulation),
            CancellationToken.None);

        Assert.Equal(KsChildRunStatus.Faulted, result.Status);
        Assert.Null(result.Response);
        Assert.NotEqual(0, result.ExitCode);
    }

    private static string GetSpikeExecutablePath()
    {
        string executablePath = Path.Combine(
            AppContext.BaseDirectory,
            "QuickPods.Spike.BluetoothKs.exe");
        Assert.True(File.Exists(executablePath));
        return executablePath;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
