using QuickPods.Spike.TaskbarHost.Presentation;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class SessionNativeCreationFailureGuardTests
{
    [Fact]
    public void ThreeConsecutiveFailuresDisableNativeForTheSession()
    {
        var guard = new SessionNativeCreationFailureGuard();

        Assert.Equal(
            NativeCreationFailureDisposition.RetryAllowed,
            guard.RecordFailure());
        Assert.Equal(
            NativeCreationFailureDisposition.RetryAllowed,
            guard.RecordFailure());
        Assert.Equal(
            NativeCreationFailureDisposition.NativeDisabledForSession,
            guard.RecordFailure());

        Assert.Equal(SessionNativeCreationFailureGuard.MaximumConsecutiveFailures, guard.ConsecutiveFailures);
        Assert.False(guard.NativeAvailable);
    }

    [Fact]
    public void AdditionalFailuresRemainCappedAndDisabled()
    {
        var guard = new SessionNativeCreationFailureGuard();
        for (int index = 0; index < 5; index++)
        {
            _ = guard.RecordFailure();
        }

        Assert.Equal(SessionNativeCreationFailureGuard.MaximumConsecutiveFailures, guard.ConsecutiveFailures);
        Assert.False(guard.NativeAvailable);
    }

    [Fact]
    public void OnlySuccessfulNativeCreationResetsConsecutiveFailures()
    {
        var guard = new SessionNativeCreationFailureGuard();
        _ = guard.RecordFailure();
        _ = guard.RecordFailure();

        Assert.Equal(2, guard.ConsecutiveFailures);
        Assert.True(guard.NativeAvailable);

        guard.RecordSuccessfulCreation();

        Assert.Equal(0, guard.ConsecutiveFailures);
        Assert.True(guard.NativeAvailable);
        Assert.Equal(
            NativeCreationFailureDisposition.RetryAllowed,
            guard.RecordFailure());
    }

    [Fact]
    public void DisabledGuardIsLatchedForTheRemainderOfTheSession()
    {
        var guard = new SessionNativeCreationFailureGuard();
        _ = guard.RecordFailure();
        _ = guard.RecordFailure();
        _ = guard.RecordFailure();
        Assert.False(guard.NativeAvailable);

        guard.RecordSuccessfulCreation();

        Assert.False(guard.NativeAvailable);
        Assert.Equal(
            SessionNativeCreationFailureGuard.MaximumConsecutiveFailures,
            guard.ConsecutiveFailures);
        Assert.Equal(
            NativeCreationFailureDisposition.NativeDisabledForSession,
            guard.RecordFailure());
    }
}
