using QuickPods.Spike.BluetoothKs.Observation;
using QuickPods.Spike.BluetoothKs.Operations;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class BluetoothOperationCoordinatorTests
{
    private static readonly TimeSpan TestCompletionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SucceedingKsRequestIsNotSuccessUntilActualStateChanges()
    {
        var clock = new ManualOperationClock();
        var observer = new StubObserver(_ => BluetoothAudioState.Disconnected);
        var invoker = new StubInvoker(_ => Task.FromResult(0));
        var coordinator = new BluetoothOperationCoordinator(invoker, observer, clock: clock);

        Task<BluetoothOperationResult> pending = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            CancellationToken.None);
        await DriveToDeadlineAsync(pending, clock);
        BluetoothOperationResult result = await pending.WaitAsync(TestCompletionTimeout);

        Assert.False(result.Succeeded);
        Assert.Equal(BluetoothOperationOutcome.DeadlineExceeded, result.Outcome);
        Assert.Equal(KsCallWatchdogStatus.Completed, result.KsCallStatus);
        Assert.Equal(0, result.KsHResult);
        Assert.Equal(BluetoothAudioState.Disconnected, result.ActualState);
        Assert.True(observer.Calls >= 2);
    }

    [Fact]
    public async Task StateIsObservedEveryTwoHundredFiftyMillisecondsUntilConnected()
    {
        var clock = new ManualOperationClock();
        var states = new Queue<BluetoothAudioState>([
            BluetoothAudioState.Disconnected,
            BluetoothAudioState.Disconnected,
            BluetoothAudioState.Connected,
        ]);
        var observer = new StubObserver(_ => states.Count > 0
            ? states.Dequeue()
            : BluetoothAudioState.Connected);
        var coordinator = new BluetoothOperationCoordinator(
            new StubInvoker(_ => Task.FromResult(0)),
            observer,
            clock: clock);

        Task<BluetoothOperationResult> pending = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            CancellationToken.None);
        await WaitUntilAsync(() => observer.Calls >= 2);
        Assert.False(pending.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        BluetoothOperationResult result = await pending.WaitAsync(TestCompletionTimeout);

        Assert.True(result.Succeeded);
        Assert.Equal(BluetoothAudioState.Connected, result.ActualState);
        Assert.Contains(TimeSpan.FromMilliseconds(250), clock.RequestedDelays);
    }

    [Fact]
    public async Task RejectedKsRequestCannotClaimAnUnrelatedDesiredStateTransition()
    {
        var states = new Queue<BluetoothAudioState>([
            BluetoothAudioState.Disconnected,
            BluetoothAudioState.Connected,
        ]);
        var coordinator = new BluetoothOperationCoordinator(
            new StubInvoker(_ => Task.FromResult(unchecked((int)0x80004005))),
            new StubObserver(_ => states.Dequeue()),
            clock: new ManualOperationClock());

        BluetoothOperationResult result = await coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BluetoothOperationOutcome.KsRequestRejected, result.Outcome);
        Assert.Equal(BluetoothAudioState.Connected, result.ActualState);
        Assert.Equal(unchecked((int)0x80004005), result.KsHResult);
    }

    [Fact]
    public async Task DisabledConnectIsClassifiedWithoutIssuingKsRequest()
    {
        var invoker = new StubInvoker(_ => Task.FromResult(0));
        var coordinator = new BluetoothOperationCoordinator(
            invoker,
            new StubObserver(_ => BluetoothAudioState.Disabled),
            clock: new ManualOperationClock());

        BluetoothOperationResult result = await coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            CancellationToken.None);

        Assert.Equal(BluetoothOperationOutcome.Disabled, result.Outcome);
        Assert.Equal(BluetoothAudioState.Disabled, result.ActualState);
        Assert.False(result.KsRequestIssued);
        Assert.Equal(0, invoker.Calls);
    }

    [Theory]
    [InlineData(BluetoothOperationKind.Connect, BluetoothAudioState.Connected)]
    [InlineData(BluetoothOperationKind.Disconnect, BluetoothAudioState.Disconnected)]
    public async Task AlreadyDesiredStateIsAnInvalidTrialRatherThanSuccess(
        BluetoothOperationKind kind,
        BluetoothAudioState initialState)
    {
        var invoker = new StubInvoker(_ => Task.FromResult(0));
        var coordinator = new BluetoothOperationCoordinator(
            invoker,
            new StubObserver(_ => initialState),
            clock: new ManualOperationClock());

        BluetoothOperationResult result = await coordinator.ExecuteAsync(
            "container-a",
            kind,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BluetoothOperationOutcome.AlreadyInDesiredState, result.Outcome);
        Assert.False(result.KsRequestIssued);
        Assert.Equal(0, invoker.Calls);
    }

    [Fact]
    public async Task DisabledEndpointIsNotReportedAsSuccessfulDisconnect()
    {
        var states = new Queue<BluetoothAudioState>([
            BluetoothAudioState.Connected,
            BluetoothAudioState.Disabled,
        ]);
        var coordinator = new BluetoothOperationCoordinator(
            new StubInvoker(_ => Task.FromResult(0)),
            new StubObserver(_ => states.Dequeue()),
            clock: new ManualOperationClock());

        BluetoothOperationResult result = await coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Disconnect,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BluetoothOperationOutcome.Disabled, result.Outcome);
        Assert.Equal(BluetoothAudioState.Disabled, result.ActualState);
    }

    [Fact]
    public async Task TransientUnpluggedStateCannotClaimSuccessfulDisconnect()
    {
        var clock = new ManualOperationClock();
        var states = new Queue<BluetoothAudioState>([
            BluetoothAudioState.Connected,
            BluetoothAudioState.Disconnected,
            BluetoothAudioState.Connected,
        ]);
        var observer = new StubObserver(_ => states.Count > 0
            ? states.Dequeue()
            : BluetoothAudioState.Connected);
        var coordinator = new BluetoothOperationCoordinator(
            new StubInvoker(_ => Task.FromResult(0)),
            observer,
            clock: clock);

        Task<BluetoothOperationResult> pending = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Disconnect,
            CancellationToken.None);
        await WaitUntilAsync(() => observer.Calls >= 2);
        Assert.False(pending.IsCompleted);

        clock.Advance(BluetoothOperationCoordinator.ObservationInterval);
        await WaitUntilAsync(() => observer.Calls >= 3);
        await DriveToDeadlineAsync(pending, clock);
        BluetoothOperationResult result = await pending.WaitAsync(TestCompletionTimeout);

        Assert.False(result.Succeeded);
        Assert.Equal(BluetoothOperationOutcome.DeadlineExceeded, result.Outcome);
        Assert.Equal(BluetoothAudioState.Connected, result.ActualState);
    }

    [Fact]
    public async Task ResultFromSupersededGenerationIsDiscardedBeforeKsRequest()
    {
        var clock = new ManualOperationClock();
        var generations = new OperationGenerationRegistry();
        var observer = new BlockingObserver();
        var invoker = new StubInvoker(_ => Task.FromResult(0));
        var coordinator = new BluetoothOperationCoordinator(
            invoker,
            observer,
            generations,
            clock: clock);

        Task<BluetoothOperationResult> pending = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            CancellationToken.None);
        await WaitUntilAsync(() => observer.RequestedGeneration is not null);
        long staleGeneration = observer.RequestedGeneration!.Value;
        _ = generations.Begin("container-a");
        observer.Complete(staleGeneration, BluetoothAudioState.Disconnected);

        BluetoothOperationResult result = await pending.WaitAsync(TestCompletionTimeout);

        Assert.Equal(BluetoothOperationOutcome.Superseded, result.Outcome);
        Assert.Equal(0, invoker.Calls);
        Assert.False(generations.TryAccept(result));
    }

    [Fact]
    public async Task CancellationDoesNotReleaseContainerWhileSynchronousKsCallIsRunning()
    {
        var nativeCompletion = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new StubInvoker(_ => nativeCompletion.Task);
        var coordinator = new BluetoothOperationCoordinator(
            invoker,
            new StubObserver(_ => BluetoothAudioState.Disconnected));
        using var firstCancellation = new CancellationTokenSource();

        Task<BluetoothOperationResult> first = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            firstCancellation.Token);
        await WaitUntilAsync(() => invoker.Calls == 1);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first.WaitAsync(TestCompletionTimeout));

        using var secondCancellation = new CancellationTokenSource();
        Task<BluetoothOperationResult> second = coordinator.ExecuteAsync(
            "container-a",
            BluetoothOperationKind.Connect,
            secondCancellation.Token);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, invoker.Calls);

        nativeCompletion.SetResult(0);
        await WaitUntilAsync(() => invoker.Calls == 2);
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => second.WaitAsync(TestCompletionTimeout));
    }

    private static async Task DriveToDeadlineAsync(
        Task operation,
        ManualOperationClock clock)
    {
        if (operation.IsCompleted)
        {
            return;
        }

        await WaitUntilAsync(() => clock.PendingDelayCount > 0 || operation.IsCompleted);
        clock.Advance(
            BluetoothOperationCoordinator.OperationDeadline +
            BluetoothOperationCoordinator.ObservationInterval);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }

        Assert.True(predicate());
    }

    private sealed class StubInvoker(Func<BluetoothOperationRequest, Task<int>> invoke)
        : IBluetoothKsCommandInvoker
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<int> InvokeAsync(BluetoothOperationRequest request)
        {
            Interlocked.Increment(ref _calls);
            return invoke(request);
        }
    }

    private sealed class StubObserver(Func<long, BluetoothAudioState> observe)
        : IBluetoothStateObserver
    {
        public int Calls { get; private set; }

        public Task<BluetoothStateObservation> ObserveAsync(
            string containerKey,
            long generation,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Observation(generation, observe(generation)));
        }
    }

    private sealed class BlockingObserver : IBluetoothStateObserver
    {
        private readonly TaskCompletionSource<BluetoothStateObservation> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long? RequestedGeneration { get; private set; }

        public Task<BluetoothStateObservation> ObserveAsync(
            string containerKey,
            long generation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedGeneration = generation;
            return _completion.Task;
        }

        public void Complete(long generation, BluetoothAudioState state)
        {
            _completion.SetResult(Observation(generation, state));
        }
    }

    private static BluetoothStateObservation Observation(
        long generation,
        BluetoothAudioState state)
    {
        BluetoothContainerEvidence evidence = state switch
        {
            BluetoothAudioState.Connected => Evidence(BluetoothEndpointState.Active),
            BluetoothAudioState.Disconnected => Evidence(BluetoothEndpointState.Unplugged),
            BluetoothAudioState.Disabled => Evidence(BluetoothEndpointState.Disabled),
            BluetoothAudioState.NotConfigured => new BluetoothContainerEvidence(
                TargetConfigured: false,
                ContainerPresent: false,
                EnumerationComplete: true,
                EndpointStates: []),
            _ => new BluetoothContainerEvidence(
                TargetConfigured: true,
                ContainerPresent: true,
                EnumerationComplete: false,
                EndpointStates: []),
        };
        return new BluetoothStateObservation(generation, evidence);
    }

    private static BluetoothContainerEvidence Evidence(BluetoothEndpointState state)
    {
        return new BluetoothContainerEvidence(
            TargetConfigured: true,
            ContainerPresent: true,
            EnumerationComplete: true,
            EndpointStates: [state]);
    }
}

internal sealed class ManualOperationClock : IOperationClock
{
    private readonly Lock _sync = new();
    private readonly List<ScheduledDelay> _delays = [];
    private long _timestamp;

    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync)
            {
                return TimeSpan.FromTicks(_timestamp);
            }
        }
    }

    public int PendingDelayCount
    {
        get
        {
            lock (_sync)
            {
                return _delays.Count(delay => !delay.Completion.Task.IsCompleted);
            }
        }
    }

    public List<TimeSpan> RequestedDelays { get; } = [];

    public long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        lock (_sync)
        {
            return TimeSpan.FromTicks(_timestamp - startingTimestamp);
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            RequestedDelays.Add(delay);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delays.Add(new ScheduledDelay(checked(_timestamp + delay.Ticks), completion));
            return completion.Task;
        }
    }

    public void Advance(TimeSpan amount)
    {
        List<TaskCompletionSource> due;
        lock (_sync)
        {
            _timestamp = checked(_timestamp + amount.Ticks);
            due = [.. _delays
                .Where(delay => delay.DueTimestamp <= _timestamp && !delay.Completion.Task.IsCompleted)
                .Select(delay => delay.Completion)];
        }

        foreach (TaskCompletionSource completion in due)
        {
            completion.TrySetResult();
        }
    }

    private sealed record ScheduledDelay(long DueTimestamp, TaskCompletionSource Completion);
}
