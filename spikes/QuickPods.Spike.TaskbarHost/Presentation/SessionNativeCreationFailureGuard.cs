namespace QuickPods.Spike.TaskbarHost.Presentation;

internal enum NativeCreationFailureDisposition
{
    RetryAllowed,
    NativeDisabledForSession,
}

/// <summary>
/// Counts only consecutive native creation failures. Placement observations do
/// not participate. A successful creation resets the count before the limit;
/// reaching the limit latches native-disabled for the rest of the session.
/// </summary>
internal sealed class SessionNativeCreationFailureGuard
{
    internal const int MaximumConsecutiveFailures = 3;

    private bool nativeDisabledForSession;

    internal int ConsecutiveFailures { get; private set; }

    internal bool NativeAvailable =>
        !nativeDisabledForSession;

    internal NativeCreationFailureDisposition RecordFailure()
    {
        if (nativeDisabledForSession)
        {
            return NativeCreationFailureDisposition.NativeDisabledForSession;
        }

        ConsecutiveFailures = Math.Min(
            checked(ConsecutiveFailures + 1),
            MaximumConsecutiveFailures);
        nativeDisabledForSession =
            ConsecutiveFailures >= MaximumConsecutiveFailures;
        return NativeAvailable
            ? NativeCreationFailureDisposition.RetryAllowed
            : NativeCreationFailureDisposition.NativeDisabledForSession;
    }

    internal void RecordSuccessfulCreation()
    {
        if (!nativeDisabledForSession)
        {
            ConsecutiveFailures = 0;
        }
    }
}
