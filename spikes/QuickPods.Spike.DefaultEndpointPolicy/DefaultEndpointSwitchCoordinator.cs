namespace QuickPods.Spike.DefaultEndpointPolicy;

internal sealed class DefaultEndpointSwitchCoordinator(
    IDefaultAudioEndpointPolicy policy,
    IDefaultEndpointGenerationFence generationFence)
{
    private static readonly DefaultEndpointRole[] MutableRoles =
    [
        DefaultEndpointRole.Console,
        DefaultEndpointRole.Multimedia,
    ];

    internal async Task<DefaultEndpointSwitchResult> SwitchAsync(
        DefaultEndpointCandidate target,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateTarget(target);

        if (!generationFence.IsCurrent(generation))
        {
            return Result(DefaultEndpointSwitchOutcome.Superseded, generation, [], true);
        }

        OpaqueEndpointHandle? originalCommunications =
            await policy.GetDefaultEndpointAsync(
                DefaultEndpointRole.Communications,
                cancellationToken).ConfigureAwait(false);
        var evidence = new List<DefaultEndpointRoleEvidence>(MutableRoles.Length);

        foreach (DefaultEndpointRole role in MutableRoles)
        {
            if (!generationFence.IsCurrent(generation))
            {
                return await FinishAsync(
                    DefaultEndpointSwitchOutcome.Superseded,
                    generation,
                    originalCommunications,
                    evidence,
                    cancellationToken).ConfigureAwait(false);
            }

            OpaqueEndpointHandle? current = await policy.GetDefaultEndpointAsync(
                role,
                cancellationToken).ConfigureAwait(false);
            if (target.Endpoint.Equals(current))
            {
                evidence.Add(new DefaultEndpointRoleEvidence(
                    role,
                    ChangeRequired: false,
                    WriteAccepted: false,
                    NotificationObserved: false,
                    ReadBackMatched: true,
                    HResult: null));
                continue;
            }

            DefaultEndpointWriteResult write =
                await policy.SetDefaultEndpointAsync(
                    target.Endpoint,
                    role,
                    cancellationToken).ConfigureAwait(false);
            if (!write.Accepted)
            {
                evidence.Add(new DefaultEndpointRoleEvidence(
                    role,
                    ChangeRequired: true,
                    WriteAccepted: false,
                    NotificationObserved: false,
                    ReadBackMatched: false,
                    HResult: write.HResult));
                DefaultEndpointSwitchOutcome rejectedOutcome = evidence.Any(item =>
                    item.WriteAccepted)
                    ? DefaultEndpointSwitchOutcome.Partial
                    : DefaultEndpointSwitchOutcome.WriteRejected;
                return await FinishAsync(
                    rejectedOutcome,
                    generation,
                    originalCommunications,
                    evidence,
                    cancellationToken).ConfigureAwait(false);
            }

            DefaultEndpointNotification notification =
                await policy.WaitForDefaultEndpointChangedAsync(
                    target.Endpoint,
                    role,
                    generation,
                    cancellationToken).ConfigureAwait(false);
            OpaqueEndpointHandle? readBack = await policy.GetDefaultEndpointAsync(
                role,
                cancellationToken).ConfigureAwait(false);
            bool notificationMatched = notification is
            {
                Observed: true,
                Role: var observedRole,
                Generation: var observedGeneration,
            } && observedRole == role && observedGeneration == generation;
            bool readBackMatched = target.Endpoint.Equals(readBack);
            evidence.Add(new DefaultEndpointRoleEvidence(
                role,
                ChangeRequired: true,
                WriteAccepted: true,
                NotificationObserved: notificationMatched,
                ReadBackMatched: readBackMatched,
                HResult: write.HResult));
            if (!generationFence.IsCurrent(generation))
            {
                return await FinishAsync(
                    DefaultEndpointSwitchOutcome.Superseded,
                    generation,
                    originalCommunications,
                    evidence,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!notificationMatched || !readBackMatched)
            {
                return await FinishAsync(
                    DefaultEndpointSwitchOutcome.VerificationFailed,
                    generation,
                    originalCommunications,
                    evidence,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        bool changed = evidence.Any(item => item.ChangeRequired);
        return await FinishAsync(
            changed
                ? DefaultEndpointSwitchOutcome.Applied
                : DefaultEndpointSwitchOutcome.AlreadyDefault,
            generation,
            originalCommunications,
            evidence,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<DefaultEndpointSwitchResult> FinishAsync(
        DefaultEndpointSwitchOutcome outcome,
        long generation,
        OpaqueEndpointHandle? originalCommunications,
        IReadOnlyList<DefaultEndpointRoleEvidence> evidence,
        CancellationToken cancellationToken)
    {
        OpaqueEndpointHandle? currentCommunications =
            await policy.GetDefaultEndpointAsync(
                DefaultEndpointRole.Communications,
                cancellationToken).ConfigureAwait(false);
        bool communicationsUnchanged = Equals(
            originalCommunications,
            currentCommunications);
        return Result(
            communicationsUnchanged
                ? outcome
                : DefaultEndpointSwitchOutcome.CommunicationsChanged,
            generation,
            evidence,
            communicationsUnchanged);
    }

    private static DefaultEndpointSwitchResult Result(
        DefaultEndpointSwitchOutcome outcome,
        long generation,
        IReadOnlyList<DefaultEndpointRoleEvidence> evidence,
        bool communicationsUnchanged) =>
        new(outcome, generation, evidence, communicationsUnchanged);

    private static void ValidateTarget(DefaultEndpointCandidate target)
    {
        if (target.Flow != AudioDataFlow.Render ||
            target.Profile != BluetoothAudioProfile.Stereo ||
            target.Availability != AudioEndpointAvailability.Active)
        {
            throw new ArgumentException(
                "Only an active stereo render endpoint can become the media default.",
                nameof(target));
        }
    }
}
