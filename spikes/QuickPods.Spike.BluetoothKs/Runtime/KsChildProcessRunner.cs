using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal enum KsChildRunStatus
{
    Completed,
    TimedOut,
    Faulted,
    ContainmentFailed,
}

internal sealed record KsChildRunResult(
    KsChildRunStatus Status,
    KsChildResponse? Response,
    int? ExitCode,
    int? ProcessId = null);

internal interface IKsChildProcessRunner
{
    Task<KsChildRunResult> RunAsync(
        KsChildInvocation invocation,
        CancellationToken cancellationToken);
}

internal sealed class KsChildProcessRunner : IKsChildProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(1);

    private readonly string? _executablePath;
    private readonly TimeSpan _timeout;
    private int _containmentCircuitOpen;

    public KsChildProcessRunner(
        TimeSpan? timeout = null,
        string? executablePath = null)
    {
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (executablePath is not null && !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The KS child executable was not found.",
                executablePath);
        }

        _executablePath = executablePath;
    }

    public async Task<KsChildRunResult> RunAsync(
        KsChildInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.TargetHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.AdapterDeviceId);
        if (Volatile.Read(ref _containmentCircuitOpen) != 0)
        {
            throw new InvalidOperationException(
                "KS operations are disabled because prior child containment could not be proven.");
        }

        string executablePath = _executablePath ?? Environment.ProcessPath ??
            throw new InvalidOperationException("The current executable path is unavailable.");
        string authorizationToken = KsChildProtocol.CreateAuthorizationToken();
        string requestNonce = KsChildProtocol.CreateRequestNonce();
        var request = new KsChildRequest(
            requestNonce,
            invocation.TargetHash,
            invocation.AdapterDeviceId,
            invocation.Operation,
            invocation.Consent);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment[KsChildProtocol.AuthorizationEnvironmentVariable] =
            authorizationToken;
        startInfo.ArgumentList.Add("--ks-child");
        startInfo.ArgumentList.Add(invocation.Operation.ToString());

        using var job = KillOnCloseJob.Create();
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The watchdog-protected KS child process could not be started.");
        int processId = process.Id;
        Task processExit = process.WaitForExitAsync(CancellationToken.None);
        try
        {
            job.Assign(process);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
                CancellationToken.None);
            Task<string> standardError = process.StandardError.ReadToEndAsync(
                CancellationToken.None);
            try
            {
                await process.StandardInput.WriteLineAsync(
                    request.Serialize(authorizationToken).AsMemory(),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                process.StandardInput.Close();
            }

            var timeout = Task.Delay(_timeout, CancellationToken.None);
            var cancelled = Task.Delay(
                System.Threading.Timeout.InfiniteTimeSpan,
                cancellationToken);
            Task completed = await Task.WhenAny(
                processExit,
                timeout,
                cancelled).ConfigureAwait(false);
            if (completed != processExit)
            {
                bool contained = await TerminateAndConfirmAsync(
                    job,
                    process,
                    processExit).ConfigureAwait(false);
                if (completed == cancelled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (!contained)
                {
                    return new KsChildRunResult(
                        KsChildRunStatus.ContainmentFailed,
                        Response: null,
                        ExitCode: null,
                        processId);
                }

                _ = await standardOutput.ConfigureAwait(false);
                _ = await standardError.ConfigureAwait(false);
                return new KsChildRunResult(
                    KsChildRunStatus.TimedOut,
                    Response: null,
                    process.ExitCode,
                    processId);
            }

            await processExit.ConfigureAwait(false);
            bool processTreeEmpty = await job.TerminateAndConfirmEmptyAsync(
                TerminationTimeout).ConfigureAwait(false);
            if (!processTreeEmpty)
            {
                Interlocked.Exchange(ref _containmentCircuitOpen, 1);
                return new KsChildRunResult(
                    KsChildRunStatus.ContainmentFailed,
                    Response: null,
                    ExitCode: process.ExitCode,
                    processId);
            }

            string output = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new KsChildRunResult(
                    KsChildRunStatus.Faulted,
                    Response: null,
                    process.ExitCode,
                    processId);
            }

            try
            {
                var response = KsChildResponse.Deserialize(
                    output.Trim(),
                    requestNonce,
                    invocation.TargetHash,
                    invocation.Operation);
                return new KsChildRunResult(
                    KsChildRunStatus.Completed,
                    response,
                    process.ExitCode,
                    processId);
            }
            catch (Exception exception) when (
                exception is JsonException or FormatException or ArgumentException)
            {
                return new KsChildRunResult(
                    KsChildRunStatus.Faulted,
                    Response: null,
                    process.ExitCode,
                    processId);
            }
        }
        catch
        {
            bool contained = await TerminateAndConfirmAsync(
                job,
                process,
                processExit).ConfigureAwait(false);
            if (!contained)
            {
                throw new InvalidOperationException(
                    "The KS child failed and its containment could not be proven.");
            }

            throw;
        }
    }

    private async Task<bool> TerminateAndConfirmAsync(
        KillOnCloseJob job,
        Process process,
        Task processExit)
    {
        TryTerminate(process);
        bool processTreeEmpty = await job.TerminateAndConfirmEmptyAsync(
            TerminationTimeout).ConfigureAwait(false);
        _ = await Task.WhenAny(
            processExit,
            Task.Delay(TerminationTimeout, CancellationToken.None)).ConfigureAwait(false);
        if (processTreeEmpty && processExit.IsCompleted)
        {
            await processExit.ConfigureAwait(false);
            return true;
        }

        Interlocked.Exchange(ref _containmentCircuitOpen, 1);
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
            // Closing the kill-on-close Job Object is the independent containment path.
        }
    }
}
