using System.Diagnostics;
using System.Globalization;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class KillOnCloseJobTests
{
    [Fact]
    public void NamedCreateRefusesToJoinAnExistingJob()
    {
        string jobName = $@"Global\QuickPods.BluetoothKs.Tests.{Guid.NewGuid():N}";
        using KillOnCloseJob first = KillOnCloseJob.Create(jobName);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => KillOnCloseJob.Create(jobName));

        Assert.Contains("refusing to join", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedJobRemainsPoisonedUntilItsDescendantTreeIsEmpty()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string jobName = $@"Global\QuickPods.BluetoothKs.Tests.{Guid.NewGuid():N}";
        var job = KillOnCloseJob.Create(jobName);
        using Process parent = StartPausedParent();
        int? descendantProcessId = null;
        try
        {
            job.Assign(parent);
            await parent.StandardInput.WriteLineAsync("spawn".AsMemory(), timeout.Token);
            parent.StandardInput.Close();
            string? processIdLine = await parent.StandardOutput.ReadLineAsync(
                timeout.Token);
            Assert.True(int.TryParse(
                processIdLine,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsedProcessId));
            descendantProcessId = parsedProcessId;
            await parent.WaitForExitAsync(timeout.Token);
            Assert.True(IsProcessRunning(parsedProcessId));

            JobRecoveryStatus recovered = await KillOnCloseJob.RecoverNamedAsync(
                jobName,
                TimeSpan.FromSeconds(2));
            Assert.Equal(JobRecoveryStatus.Recovered, recovered);
            Assert.False(IsProcessRunning(parsedProcessId));

            job.Dispose();
            JobRecoveryStatus cleared = await KillOnCloseJob.RecoverNamedAsync(
                jobName,
                TimeSpan.FromSeconds(2));
            Assert.Equal(JobRecoveryStatus.NoPriorJob, cleared);
        }
        finally
        {
            job.Dispose();
            if (!parent.HasExited)
            {
                parent.Kill(entireProcessTree: true);
                await parent.WaitForExitAsync(CancellationToken.None);
            }

            if (descendantProcessId is int processId && IsProcessRunning(processId))
            {
                using Process descendant = Process.GetProcessById(processId);
                descendant.Kill(entireProcessTree: true);
                await descendant.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static Process StartPausedParent()
    {
        const string command =
            "$null = [Console]::In.ReadLine(); " +
            "$child = Start-Process -FilePath 'ping.exe' -ArgumentList '-t','127.0.0.1' -PassThru; " +
            "[Console]::Out.WriteLine($child.Id)";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The paused job-test parent could not be started.");
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
