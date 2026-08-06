using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using QuickPods.Core.Models;

namespace QuickPods.Windows.Bluetooth.Worker;

internal enum BluetoothWorkerRunStatus
{
    Completed,
    TimedOut,
    Faulted,
    ContainmentFailed,
}

internal sealed record BluetoothWorkerRunResult(
    BluetoothWorkerRunStatus Status,
    BluetoothWorkerResponse? Response,
    int? ExitCode);

internal sealed class BluetoothWorkerProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(1);

    private readonly string workerPath;
    private readonly TimeSpan timeout;
    private int containmentCircuitOpen;

    internal BluetoothWorkerProcessRunner(string workerPath, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        this.workerPath = Path.GetFullPath(workerPath);
        this.timeout = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.timeout, TimeSpan.Zero);
    }

    internal async Task<BluetoothWorkerRunResult> RunAsync(
        BluetoothDeviceKey targetKey,
        string adapterDeviceId,
        BluetoothWorkerOperation operation,
        bool mutationConfirmed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterDeviceId);
        if (Volatile.Read(ref containmentCircuitOpen) != 0)
        {
            throw new InvalidOperationException(
                "Bluetooth worker execution is disabled because containment could not be proven.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(workerPath))
        {
            return new(BluetoothWorkerRunStatus.Faulted, null, null);
        }

        string token = BluetoothWorkerProtocol.CreateToken();
        var request = new BluetoothWorkerRequest(
            BluetoothWorkerProtocol.CreateToken(),
            targetKey,
            adapterDeviceId,
            operation,
            mutationConfirmed);
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment[BluetoothWorkerProtocol.AuthorizationEnvironmentVariable] = token;
        startInfo.ArgumentList.Add("--operation");
        startInfo.ArgumentList.Add(operation.ToString());

        using BluetoothWorkerJob job = BluetoothWorkerJob.Create();
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The Bluetooth worker process could not be started.");
        Task processExit = process.WaitForExitAsync(CancellationToken.None);
        try
        {
            job.Assign(process);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            await process.StandardInput.WriteLineAsync(request.Serialize(token).AsMemory(), CancellationToken.None)
                .ConfigureAwait(false);
            process.StandardInput.Close();

            Task completed = await Task.WhenAny(
                processExit,
                Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
            if (completed != processExit)
            {
                bool contained = await TerminateAndConfirmAsync(job, process, processExit).ConfigureAwait(false);
                _ = await standardOutput.ConfigureAwait(false);
                _ = await standardError.ConfigureAwait(false);
                return new(
                    contained
                        ? BluetoothWorkerRunStatus.TimedOut
                        : BluetoothWorkerRunStatus.ContainmentFailed,
                    null,
                    process.HasExited ? process.ExitCode : null);
            }

            await processExit.ConfigureAwait(false);
            bool empty = await job.TerminateAndConfirmEmptyAsync(TerminationTimeout).ConfigureAwait(false);
            if (!empty)
            {
                Interlocked.Exchange(ref containmentCircuitOpen, 1);
                return new(BluetoothWorkerRunStatus.ContainmentFailed, null, process.ExitCode);
            }

            string output = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new(BluetoothWorkerRunStatus.Faulted, null, process.ExitCode);
            }

            try
            {
                BluetoothWorkerResponse response = BluetoothWorkerResponse.DeserializeAndValidate(
                    output.Trim(),
                    request);
                return new(BluetoothWorkerRunStatus.Completed, response, process.ExitCode);
            }
            catch (Exception exception) when (
                exception is JsonException or FormatException or ArgumentException)
            {
                return new(BluetoothWorkerRunStatus.Faulted, null, process.ExitCode);
            }
        }
        catch
        {
            bool contained = await TerminateAndConfirmAsync(job, process, processExit).ConfigureAwait(false);
            if (!contained)
            {
                throw new InvalidOperationException(
                    "Bluetooth worker containment could not be proven.");
            }

            throw;
        }
    }

    private async Task<bool> TerminateAndConfirmAsync(
        BluetoothWorkerJob job,
        Process process,
        Task processExit)
    {
        TryTerminate(process);
        bool empty = await job.TerminateAndConfirmEmptyAsync(TerminationTimeout).ConfigureAwait(false);
        _ = await Task.WhenAny(
            processExit,
            Task.Delay(TerminationTimeout, CancellationToken.None)).ConfigureAwait(false);
        if (empty && processExit.IsCompleted)
        {
            await processExit.ConfigureAwait(false);
            return true;
        }

        Interlocked.Exchange(ref containmentCircuitOpen, 1);
        return false;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
