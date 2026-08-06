using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Operations;

public sealed class BluetoothOperationCoordinator
{
    public static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DisconnectStabilityWindow = TimeSpan.FromSeconds(5);

    private readonly IBluetoothKsCommandInvoker _commandInvoker;
    private readonly IBluetoothStateObserver _stateObserver;
    private readonly OperationGenerationRegistry _generations;
    private readonly ContainerOperationSerializer _serializer;
    private readonly IOperationClock _clock;
    private readonly KsCallWatchdog _watchdog;

    public BluetoothOperationCoordinator(
        IBluetoothKsCommandInvoker commandInvoker,
        IBluetoothStateObserver stateObserver,
        OperationGenerationRegistry? generations = null,
        ContainerOperationSerializer? serializer = null,
        IOperationClock? clock = null)
    {
        _commandInvoker = commandInvoker ?? throw new ArgumentNullException(nameof(commandInvoker));
        _stateObserver = stateObserver ?? throw new ArgumentNullException(nameof(stateObserver));
        _generations = generations ?? new OperationGenerationRegistry();
        _serializer = serializer ?? new ContainerOperationSerializer();
        _clock = clock ?? SystemOperationClock.Instance;
        _watchdog = new KsCallWatchdog(_clock);
    }

    public async Task<BluetoothOperationResult> ExecuteAsync(
        string containerKey,
        BluetoothOperationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);

        long generation = _generations.Begin(containerKey);
        var request = new BluetoothOperationRequest(
            containerKey,
            kind,
            generation,
            _clock.GetTimestamp());

        ContainerOperationLease? lease = await AcquireBeforeDeadlineAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return CreateResult(
                request,
                BluetoothOperationOutcome.DeadlineExceeded,
                BluetoothAudioState.Unknown,
                KsCallWatchdogStatus.NotStarted,
                hResult: null,
                requestIssued: false);
        }

        await using (lease.ConfigureAwait(false))
        {
            return await ExecuteSerializedAsync(request, lease, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<BluetoothOperationResult> ExecuteSerializedAsync(
        BluetoothOperationRequest request,
        ContainerOperationLease lease,
        CancellationToken cancellationToken)
    {
        BluetoothAudioState actualState = BluetoothAudioState.Unknown;
        KsCallWatchdogStatus callStatus = KsCallWatchdogStatus.NotStarted;
        int? hResult = null;
        bool requestIssued = false;
        Task<int>? activeCallCompletion = null;

        try
        {
            ObservationAttempt initial = await ObserveUntilKnownAsync(
                request,
                lease,
                cancellationToken).ConfigureAwait(false);
            actualState = initial.State;

            if (!_generations.IsCurrent(request.ContainerKey, request.Generation))
            {
                return CreateSupersededResult(request, actualState);
            }

            if (initial.DeadlineExpired)
            {
                return CreateResult(
                    request,
                    BluetoothOperationOutcome.DeadlineExceeded,
                    actualState,
                    KsCallWatchdogStatus.NotStarted,
                    hResult: null,
                    requestIssued: false);
            }

            BluetoothOperationOutcome? preflightOutcome = ClassifyPreflight(request.Kind, actualState);
            if (preflightOutcome is not null)
            {
                return CreateResult(
                    request,
                    preflightOutcome.Value,
                    actualState,
                    KsCallWatchdogStatus.NotStarted,
                    hResult: null,
                    requestIssued: false);
            }

            if (GetRemaining(request) < KsCallWatchdog.Timeout)
            {
                return CreateResult(
                    request,
                    BluetoothOperationOutcome.DeadlineExceeded,
                    actualState,
                    KsCallWatchdogStatus.NotStarted,
                    hResult: null,
                    requestIssued: false);
            }

            Task<int> callCompletion = _commandInvoker.InvokeAsync(request);
            activeCallCompletion = callCompletion;
            requestIssued = true;
            KsCallWatchdogResult call = await _watchdog.WaitAsync(
                callCompletion,
                cancellationToken).ConfigureAwait(false);
            callStatus = call.Status;
            hResult = call.HResult;

            if (!_generations.IsCurrent(request.ContainerKey, request.Generation))
            {
                if (!callCompletion.IsCompleted)
                {
                    lease.HoldUntil(callCompletion);
                }

                return CreateSupersededResult(request, actualState, call.Status, call.HResult, requestIssued: true);
            }

            if (call.Status == KsCallWatchdogStatus.TimedOut)
            {
                lease.HoldUntil(callCompletion);
                return CreateResult(
                    request,
                    BluetoothOperationOutcome.KsWatchdogTimedOut,
                    actualState,
                    call.Status,
                    call.HResult,
                    requestIssued: true);
            }

            if (call.Status == KsCallWatchdogStatus.Faulted)
            {
                return CreateResult(
                    request,
                    BluetoothOperationOutcome.Faulted,
                    actualState,
                    call.Status,
                    call.HResult,
                    requestIssued: true);
            }

            ObservationAttempt final = await ObserveUntilDesiredOrDeadlineAsync(
                request,
                lease,
                actualState,
                cancellationToken).ConfigureAwait(false);
            actualState = final.State;

            if (!_generations.IsCurrent(request.ContainerKey, request.Generation))
            {
                return CreateSupersededResult(request, actualState, call.Status, call.HResult, requestIssued: true);
            }

            if (!final.DeadlineExpired &&
                IsDesiredState(request.Kind, actualState) &&
                call.HResult == 0)
            {
                return CreateResult(
                    request,
                    BluetoothOperationOutcome.Succeeded,
                    actualState,
                    call.Status,
                    call.HResult,
                    requestIssued: true);
            }

            BluetoothOperationOutcome failure = actualState switch
            {
                BluetoothAudioState.NotConfigured => BluetoothOperationOutcome.NotConfigured,
                BluetoothAudioState.Disabled => BluetoothOperationOutcome.Disabled,
                _ when call.HResult != 0 => BluetoothOperationOutcome.KsRequestRejected,
                _ => BluetoothOperationOutcome.DeadlineExceeded,
            };
            return CreateResult(
                request,
                failure,
                actualState,
                call.Status,
                call.HResult,
                requestIssued: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (activeCallCompletion is { IsCompleted: false })
            {
                lease.HoldUntil(activeCallCompletion);
            }

            throw;
        }
        catch
        {
            if (activeCallCompletion is { IsCompleted: false })
            {
                lease.HoldUntil(activeCallCompletion);
            }

            return CreateResult(
                request,
                BluetoothOperationOutcome.Faulted,
                actualState,
                callStatus,
                hResult,
                requestIssued);
        }
    }

    private async Task<ContainerOperationLease?> AcquireBeforeDeadlineAsync(
        BluetoothOperationRequest request,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = GetRemaining(request);
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ContainerOperationLease> acquire = _serializer.AcquireAsync(
            request.ContainerKey,
            waitCancellation.Token);
        Task deadline = _clock.DelayAsync(remaining, deadlineCancellation.Token);
        _ = await Task.WhenAny(acquire, deadline).ConfigureAwait(false);

        if (acquire.IsCompletedSuccessfully)
        {
            deadlineCancellation.Cancel();
            return await acquire.ConfigureAwait(false);
        }

        await deadline.ConfigureAwait(false);
        waitCancellation.Cancel();
        try
        {
            ContainerOperationLease acquiredAfterDeadline = await acquire.ConfigureAwait(false);
            await acquiredAfterDeadline.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
        {
        }

        return null;
    }

    private async Task<ObservationAttempt> ObserveUntilKnownAsync(
        BluetoothOperationRequest request,
        ContainerOperationLease lease,
        CancellationToken cancellationToken)
    {
        BluetoothAudioState state = BluetoothAudioState.Unknown;
        while (GetRemaining(request) > TimeSpan.Zero)
        {
            ObservationAttempt observation = await ObserveOnceAsync(
                request,
                lease,
                cancellationToken).ConfigureAwait(false);
            if (observation.DeadlineExpired)
            {
                return observation with { State = state };
            }

            if (!observation.Stale && observation.State != BluetoothAudioState.Unknown)
            {
                return observation;
            }

            state = observation.State;
            await DelayForNextObservationAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return new ObservationAttempt(state, Stale: false, DeadlineExpired: true);
    }

    private async Task<ObservationAttempt> ObserveUntilDesiredOrDeadlineAsync(
        BluetoothOperationRequest request,
        ContainerOperationLease lease,
        BluetoothAudioState initialState,
        CancellationToken cancellationToken)
    {
        BluetoothAudioState state = initialState;
        long? desiredSince = null;
        while (GetRemaining(request) > TimeSpan.Zero)
        {
            ObservationAttempt observation = await ObserveOnceAsync(
                request,
                lease,
                cancellationToken).ConfigureAwait(false);
            if (observation.DeadlineExpired)
            {
                return observation with { State = state };
            }

            if (!observation.Stale)
            {
                state = observation.State;
                if (IsDesiredState(request.Kind, state))
                {
                    if (request.Kind == BluetoothOperationKind.Connect)
                    {
                        return observation;
                    }

                    desiredSince ??= _clock.GetTimestamp();
                    if (_clock.GetElapsedTime(desiredSince.Value) >= DisconnectStabilityWindow)
                    {
                        return observation;
                    }
                }
                else
                {
                    desiredSince = null;
                }

                if (state == BluetoothAudioState.NotConfigured)
                {
                    return observation;
                }
            }

            if (request.Kind == BluetoothOperationKind.Connect &&
                state == BluetoothAudioState.Disabled)
            {
                return observation;
            }

            if (request.Kind == BluetoothOperationKind.Disconnect &&
                state == BluetoothAudioState.Disabled)
            {
                return observation;
            }

            if (!_generations.IsCurrent(request.ContainerKey, request.Generation))
            {
                return new ObservationAttempt(state, Stale: true, DeadlineExpired: false);
            }

            await DelayForNextObservationAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return new ObservationAttempt(state, Stale: false, DeadlineExpired: true);
    }

    private async Task<ObservationAttempt> ObserveOnceAsync(
        BluetoothOperationRequest request,
        ContainerOperationLease lease,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = GetRemaining(request);
        if (remaining <= TimeSpan.Zero)
        {
            return new ObservationAttempt(BluetoothAudioState.Unknown, Stale: false, DeadlineExpired: true);
        }

        Task<BluetoothStateObservation> observation = _stateObserver.ObserveAsync(
            request.ContainerKey,
            request.Generation,
            cancellationToken);
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task deadline = _clock.DelayAsync(remaining, deadlineCancellation.Token);
        _ = await Task.WhenAny(observation, deadline).ConfigureAwait(false);

        if (observation.IsCompleted)
        {
            deadlineCancellation.Cancel();
            BluetoothStateObservation value = await observation.ConfigureAwait(false);
            bool stale = value.Generation != request.Generation;
            BluetoothAudioState state = stale
                ? BluetoothAudioState.Unknown
                : BluetoothStateClassifier.Classify(value.Evidence);
            return new ObservationAttempt(state, stale, DeadlineExpired: false);
        }

        try
        {
            await deadline.ConfigureAwait(false);
        }
        catch
        {
            if (!observation.IsCompleted)
            {
                lease.HoldUntil(observation);
            }

            throw;
        }

        lease.HoldUntil(observation);
        return new ObservationAttempt(BluetoothAudioState.Unknown, Stale: false, DeadlineExpired: true);
    }

    private async Task DelayForNextObservationAsync(
        BluetoothOperationRequest request,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = GetRemaining(request);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await _clock.DelayAsync(
            remaining < ObservationInterval ? remaining : ObservationInterval,
            cancellationToken).ConfigureAwait(false);
    }

    private BluetoothOperationResult CreateSupersededResult(
        BluetoothOperationRequest request,
        BluetoothAudioState state,
        KsCallWatchdogStatus callStatus = KsCallWatchdogStatus.NotStarted,
        int? hResult = null,
        bool requestIssued = false)
    {
        return CreateResult(
            request,
            BluetoothOperationOutcome.Superseded,
            state,
            callStatus,
            hResult,
            requestIssued);
    }

    private BluetoothOperationResult CreateResult(
        BluetoothOperationRequest request,
        BluetoothOperationOutcome outcome,
        BluetoothAudioState state,
        KsCallWatchdogStatus callStatus,
        int? hResult,
        bool requestIssued)
    {
        return new BluetoothOperationResult(
            request,
            outcome,
            state,
            callStatus,
            hResult,
            requestIssued,
            _clock.GetElapsedTime(request.StartedTimestamp));
    }

    private TimeSpan GetRemaining(BluetoothOperationRequest request)
    {
        return OperationDeadline - _clock.GetElapsedTime(request.StartedTimestamp);
    }

    private static BluetoothOperationOutcome? ClassifyPreflight(
        BluetoothOperationKind kind,
        BluetoothAudioState state)
    {
        if (IsDesiredState(kind, state))
        {
            return BluetoothOperationOutcome.AlreadyInDesiredState;
        }

        return state switch
        {
            BluetoothAudioState.NotConfigured => BluetoothOperationOutcome.NotConfigured,
            BluetoothAudioState.Disabled => BluetoothOperationOutcome.Disabled,
            BluetoothAudioState.Unknown => BluetoothOperationOutcome.Unknown,
            _ => null,
        };
    }

    private static bool IsDesiredState(BluetoothOperationKind kind, BluetoothAudioState state)
    {
        return kind == BluetoothOperationKind.Connect
            ? state == BluetoothAudioState.Connected
            : state == BluetoothAudioState.Disconnected;
    }

    private sealed record ObservationAttempt(
        BluetoothAudioState State,
        bool Stale,
        bool DeadlineExpired);
}
