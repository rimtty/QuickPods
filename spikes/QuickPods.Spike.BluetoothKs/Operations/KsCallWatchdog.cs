namespace QuickPods.Spike.BluetoothKs.Operations;

public enum KsCallWatchdogStatus
{
    NotStarted,
    Completed,
    TimedOut,
    Faulted,
}

public sealed record KsCallWatchdogResult(
    KsCallWatchdogStatus Status,
    int? HResult);

public sealed class KsCallWatchdog
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly IOperationClock _clock;

    public KsCallWatchdog(IOperationClock? clock = null)
    {
        _clock = clock ?? SystemOperationClock.Instance;
    }

    public async Task<KsCallWatchdogResult> WaitAsync(
        Task<int> callCompletion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callCompletion);

        Task timeout = _clock.DelayAsync(Timeout, cancellationToken);
        _ = await Task.WhenAny(callCompletion, timeout).ConfigureAwait(false);

        if (callCompletion.IsCompleted)
        {
            try
            {
                int result = await callCompletion.ConfigureAwait(false);
                return new KsCallWatchdogResult(KsCallWatchdogStatus.Completed, result);
            }
            catch (BluetoothKsInvocationTimedOutException)
            {
                return new KsCallWatchdogResult(KsCallWatchdogStatus.TimedOut, HResult: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new KsCallWatchdogResult(KsCallWatchdogStatus.Faulted, HResult: null);
            }
        }

        await timeout.ConfigureAwait(false);
        return new KsCallWatchdogResult(KsCallWatchdogStatus.TimedOut, HResult: null);
    }
}
