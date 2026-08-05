using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Presentation;

internal readonly record struct NativePromotionCandidate(
    TaskbarHostIdentity TaskbarIdentity,
    PixelRect Bounds,
    TaskbarStripMode Mode)
{
    internal bool IsValid =>
        TaskbarIdentity.TaskbarHandle != nint.Zero &&
        TaskbarIdentity.ExplorerProcessId != 0 &&
        TaskbarIdentity.Dpi != 0 &&
        TaskbarIdentity.TaskbarBounds.IsValid &&
        Bounds.IsValid &&
        Enum.IsDefined(Mode);

    /// <summary>
    /// Prevents accidental diagnostic interpolation from exposing the raw
    /// taskbar HWND or Explorer PID carried by the control-only identity.
    /// </summary>
    public override string ToString() => $"NativePromotionCandidate {{ Mode = {Mode} }}";
}

internal readonly record struct NativePromotionObservation(
    PlacementDecision Decision,
    NativePromotionCandidate? Candidate,
    bool InvalidatedDuringScan,
    bool LayoutInvalidated,
    TimeSpan Elapsed);

/// <summary>
/// Requires a cooldown followed by two identical, stable native placement
/// observations separated in monotonic time. It performs no I/O and owns no
/// Windows resources.
/// </summary>
internal sealed class NativePromotionGate
{
    internal static readonly TimeSpan ModeSwitchCooldown = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MinimumConfirmationInterval =
        TimeSpan.FromMilliseconds(500);

    private NativePromotionCandidate? candidate;
    private TimeSpan candidateObservedAt;
    private TimeSpan cooldownStartedAt;
    private TimeSpan lastElapsed;

    internal NativePromotionGate(TimeSpan modeSwitchElapsed)
    {
        ValidateNonNegative(modeSwitchElapsed, nameof(modeSwitchElapsed));
        cooldownStartedAt = modeSwitchElapsed;
        lastElapsed = modeSwitchElapsed;
    }

    internal bool HasCandidate => candidate is not null;

    internal void BeginModeSwitch(TimeSpan elapsed)
    {
        ValidateMonotonic(elapsed);
        cooldownStartedAt = elapsed;
        ResetCandidate();
    }

    internal bool Observe(NativePromotionObservation observation)
    {
        ValidateObservation(observation);
        lastElapsed = observation.Elapsed;

        if (observation.Elapsed - cooldownStartedAt < ModeSwitchCooldown ||
            observation.Decision != PlacementDecision.Place ||
            observation.InvalidatedDuringScan ||
            observation.LayoutInvalidated ||
            observation.Candidate is not NativePromotionCandidate observedCandidate ||
            !observedCandidate.IsValid)
        {
            ResetCandidate();
            return false;
        }

        if (candidate is not NativePromotionCandidate currentCandidate ||
            currentCandidate != observedCandidate)
        {
            candidate = observedCandidate;
            candidateObservedAt = observation.Elapsed;
            return false;
        }

        if (observation.Elapsed - candidateObservedAt < MinimumConfirmationInterval)
        {
            return false;
        }

        ResetCandidate();
        return true;
    }

    private void ValidateObservation(NativePromotionObservation observation)
    {
        if (!Enum.IsDefined(observation.Decision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observation),
                "The placement decision is invalid.");
        }

        ValidateMonotonic(observation.Elapsed);
    }

    private void ValidateMonotonic(TimeSpan elapsed)
    {
        ValidateNonNegative(elapsed, nameof(elapsed));
        if (elapsed < lastElapsed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                "Elapsed time must be monotonic.");
        }

        lastElapsed = elapsed;
    }

    private static void ValidateNonNegative(TimeSpan elapsed, string parameterName)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Elapsed time cannot be negative.");
        }
    }

    private void ResetCandidate()
    {
        candidate = null;
        candidateObservedAt = default;
    }
}
