using System.Diagnostics;
using System.Text.Json;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal enum IsolatedCommandStatus
{
    Completed,
    TimedOut,
    Faulted,
    ContainmentFailed,
}

internal sealed record IsolatedCommandRunResult(
    IsolatedCommandStatus Status,
    IsolatedCommandResponse? Response);

internal sealed class IsolatedCommandProcessRunner
{
    internal const string JobName =
        @"Global\QuickPods.BluetoothKs.Spike.CommandJob.v1";

    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(1);

    private readonly string _executablePath;

    internal IsolatedCommandProcessRunner(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath ??
            throw new InvalidOperationException("The current executable path is unavailable.");
        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException(
                "The isolated command executable was not found.",
                _executablePath);
        }
    }

    internal async Task<IsolatedCommandRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        string authorizationToken = KsChildProtocol.CreateAuthorizationToken();
        string requestNonce = KsChildProtocol.CreateRequestNonce();
        var request = new IsolatedCommandRequest(requestNonce, arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment[IsolatedCommandProtocol.AuthorizationEnvironmentVariable] =
            authorizationToken;
        startInfo.ArgumentList.Add("--isolated-command");

        using var job = KillOnCloseJob.Create(JobName);
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The isolated command process could not be started.");
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

            var timeoutTask = Task.Delay(timeout, CancellationToken.None);
            var cancelled = Task.Delay(
                System.Threading.Timeout.InfiniteTimeSpan,
                cancellationToken);
            Task completed = await Task.WhenAny(
                processExit,
                timeoutTask,
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

                return new IsolatedCommandRunResult(
                    contained
                        ? IsolatedCommandStatus.TimedOut
                        : IsolatedCommandStatus.ContainmentFailed,
                    Response: null);
            }

            await processExit.ConfigureAwait(false);
            bool processTreeEmpty = await job.TerminateAndConfirmEmptyAsync(
                TerminationTimeout).ConfigureAwait(false);
            if (!processTreeEmpty)
            {
                return new IsolatedCommandRunResult(
                    IsolatedCommandStatus.ContainmentFailed,
                    Response: null);
            }

            string output = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new IsolatedCommandRunResult(
                    IsolatedCommandStatus.Faulted,
                    Response: null);
            }

            try
            {
                var response = IsolatedCommandResponse.Deserialize(
                    output.Trim(),
                    requestNonce);
                return new IsolatedCommandRunResult(
                    IsolatedCommandStatus.Completed,
                    response);
            }
            catch (Exception exception) when (
                exception is JsonException or FormatException or ArgumentException)
            {
                return new IsolatedCommandRunResult(
                    IsolatedCommandStatus.Faulted,
                    Response: null);
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
                    "The isolated command failed and its containment could not be proven.");
            }

            throw;
        }
    }

    private static async Task<bool> TerminateAndConfirmAsync(
        KillOnCloseJob job,
        Process process,
        Task processExit)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The kill-on-close Job Object is the independent containment path.
        }

        bool processTreeEmpty = await job.TerminateAndConfirmEmptyAsync(
            TerminationTimeout).ConfigureAwait(false);
        _ = await Task.WhenAny(
            processExit,
            Task.Delay(TerminationTimeout, CancellationToken.None)).ConfigureAwait(false);
        if (!processTreeEmpty || !processExit.IsCompleted)
        {
            return false;
        }

        await processExit.ConfigureAwait(false);
        return true;
    }

    internal static Task<JobRecoveryStatus> RecoverPriorProcessTreeAsync() =>
        KillOnCloseJob.RecoverNamedAsync(JobName, TerminationTimeout);
}
