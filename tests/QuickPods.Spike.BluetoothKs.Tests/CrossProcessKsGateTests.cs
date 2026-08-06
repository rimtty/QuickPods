using System.Diagnostics;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class CrossProcessKsGateTests
{
    [Fact]
    public async Task FixedNamedGateRejectsASecondExecutableProcess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using Process first = StartGateChild(holdMilliseconds: 3_000);
        try
        {
            string? firstLine = await first.StandardOutput.ReadLineAsync(
                timeout.Token);
            Assert.Equal("acquired", firstLine);

            using Process second = StartGateChild(holdMilliseconds: 1);
            string secondOutput = await second.StandardOutput.ReadToEndAsync(
                timeout.Token);
            await second.WaitForExitAsync(timeout.Token);

            Assert.Equal(3, second.ExitCode);
            Assert.Equal("busy", secondOutput.Trim());

            await first.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, first.ExitCode);
        }
        finally
        {
            if (!first.HasExited)
            {
                first.Kill(entireProcessTree: true);
                await first.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task AbandonedOwnerIsRejectedBeforeANewOperationCanRun()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ownerAcquired = new ManualResetEventSlim();
        Mutex? abandonedHandle = null;
        bool operationRan = false;
        var owner = new Thread(() =>
        {
            abandonedHandle = new Mutex(
                initiallyOwned: false,
                CrossProcessKsGate.MutexName);
            _ = abandonedHandle.WaitOne();
            ownerAcquired.Set();
        });
        owner.Start();
        try
        {
            Assert.True(ownerAcquired.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(owner.Join(TimeSpan.FromSeconds(5)));

            CrossProcessKsGateResult<int> abandoned = await CrossProcessKsGate.RunAsync(
                () =>
                {
                    operationRan = true;
                    return 0;
                },
                timeout.Token);
            Assert.Equal(CrossProcessKsGateStatus.Abandoned, abandoned.Status);
            Assert.False(operationRan);

            CrossProcessKsGateResult<int> next = await CrossProcessKsGate.RunAsync(
                () => 42,
                timeout.Token);
            Assert.Equal(CrossProcessKsGateStatus.Executed, next.Status);
            Assert.Equal(42, next.Value);
        }
        finally
        {
            abandonedHandle?.Dispose();
        }
    }

    [Fact]
    public async Task CancellationNeverRunsTheOperation()
    {
        bool operationRan = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CrossProcessKsGate.RunAsync(
                () =>
                {
                    operationRan = true;
                    return 0;
                },
                cancellation.Token));
        Assert.False(operationRan);
    }

    private static Process StartGateChild(int holdMilliseconds)
    {
        string executablePath = Path.Combine(
            Path.GetDirectoryName(typeof(BluetoothKsCli).Assembly.Location)!,
            "QuickPods.Spike.BluetoothKs.exe");
        Assert.True(File.Exists(executablePath), $"Missing spike executable: {executablePath}");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(CrossProcessKsGateTestEntryPoint.Switch);
        startInfo.ArgumentList.Add(holdMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The gate-test child could not be started.");
    }
}
