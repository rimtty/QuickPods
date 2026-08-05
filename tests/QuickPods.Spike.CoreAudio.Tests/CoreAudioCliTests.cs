namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class CoreAudioCliTests
{
    [Fact]
    public void GuardedCompletionSucceedsAfterVerifiedRestoration()
    {
        CoreAudioCli.CompleteGuardedMutation(operationFailure: null, restored: true);
    }

    [Fact]
    public void GuardedCompletionRethrowsOperationFailureAfterVerifiedRestoration()
    {
        var operationFailure = new OperationCanceledException("Synthetic cancellation.");

        OperationCanceledException observed = Assert.Throws<OperationCanceledException>(() =>
            CoreAudioCli.CompleteGuardedMutation(operationFailure, restored: true));

        Assert.Same(operationFailure, observed);
    }

    [Fact]
    public void GuardedCompletionFailsWhenRestorationCannotBeVerified()
    {
        Assert.Throws<AudioRestorationException>(() =>
            CoreAudioCli.CompleteGuardedMutation(operationFailure: null, restored: false));
    }

    [Fact]
    public void GuardedCompletionPreservesBothOperationAndRestorationFailures()
    {
        var operationFailure = new TimeoutException("Synthetic operation failure.");

        AggregateException observed = Assert.Throws<AggregateException>(() =>
            CoreAudioCli.CompleteGuardedMutation(operationFailure, restored: false));

        Assert.Same(operationFailure, observed.InnerExceptions[0]);
        Assert.IsType<AudioRestorationException>(observed.InnerExceptions[1]);
    }
}
