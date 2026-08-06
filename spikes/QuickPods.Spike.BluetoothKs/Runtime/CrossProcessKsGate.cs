namespace QuickPods.Spike.BluetoothKs.Runtime;

internal enum CrossProcessKsGateStatus
{
    Executed,
    Busy,
    Abandoned,
    ContainmentUnproven,
}

internal readonly record struct CrossProcessKsGateResult<T>(
    CrossProcessKsGateStatus Status,
    T Value);

internal static class CrossProcessKsGate
{
    internal const string MutexName =
        @"Global\QuickPods.BluetoothKs.Spike.KsGate.v1";

    private static readonly TimeSpan AcquisitionTimeout =
        TimeSpan.FromMilliseconds(250);

    internal static Task<CrossProcessKsGateResult<T>> RunAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.Factory.StartNew(
            () => Run(operation, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static CrossProcessKsGateResult<T> Run<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool acquired = false;
        try
        {
            int signaled = 0;
            bool wasAbandoned = false;
            try
            {
                signaled = WaitHandle.WaitAny(
                    [mutex, cancellationToken.WaitHandle],
                    AcquisitionTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                wasAbandoned = true;
            }

            if (signaled == WaitHandle.WaitTimeout)
            {
                return new CrossProcessKsGateResult<T>(
                    CrossProcessKsGateStatus.Busy,
                    default!);
            }

            if (signaled != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "The cross-process KS gate returned an unexpected wait result.");
            }

            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            JobRecoveryStatus recovery = IsolatedCommandProcessRunner
                .RecoverPriorProcessTreeAsync()
                .GetAwaiter()
                .GetResult();
            if (recovery == JobRecoveryStatus.Unproven)
            {
                return new CrossProcessKsGateResult<T>(
                    CrossProcessKsGateStatus.ContainmentUnproven,
                    default!);
            }

            if (wasAbandoned || recovery == JobRecoveryStatus.Recovered)
            {
                return new CrossProcessKsGateResult<T>(
                    CrossProcessKsGateStatus.Abandoned,
                    default!);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new CrossProcessKsGateResult<T>(
                CrossProcessKsGateStatus.Executed,
                operation());
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
