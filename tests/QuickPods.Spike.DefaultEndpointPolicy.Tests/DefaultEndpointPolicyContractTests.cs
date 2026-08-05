using QuickPods.Spike.DefaultEndpointPolicy;

namespace QuickPods.Spike.DefaultEndpointPolicy.Tests;

public sealed class DefaultEndpointPolicyContractTests
{
    [Fact]
    public void ResolverSelectsOnlyOneActiveStereoRenderEndpoint()
    {
        var selected = new OpaqueContainerHandle("selected");
        DefaultEndpointCandidate stereo = Candidate(
            "stereo",
            selected,
            BluetoothAudioProfile.Stereo);
        DefaultEndpointCandidate handsFree = Candidate(
            "hands-free",
            selected,
            BluetoothAudioProfile.HandsFree);
        DefaultEndpointCandidate otherContainer = Candidate(
            "other",
            new OpaqueContainerHandle("other-container"),
            BluetoothAudioProfile.Stereo);

        DefaultEndpointTargetResolution resolved =
            DefaultEndpointTargetResolver.Resolve(
                [handsFree, otherContainer, stereo],
                selected);
        DefaultEndpointTargetResolution ambiguous =
            DefaultEndpointTargetResolver.Resolve(
                [stereo, Candidate("second-stereo", selected, BluetoothAudioProfile.Stereo)],
                selected);

        Assert.Equal(DefaultEndpointTargetStatus.Selected, resolved.Status);
        Assert.Same(stereo, resolved.Target);
        Assert.Equal(
            DefaultEndpointTargetStatus.AmbiguousActiveStereoEndpoints,
            ambiguous.Status);
        Assert.Null(ambiguous.Target);
    }

    [Fact]
    public async Task CoordinatorChangesOnlyConsoleAndMultimediaAfterVerifiedNotifications()
    {
        var target = new OpaqueEndpointHandle("target");
        var policy = new StubPolicy(
            console: new OpaqueEndpointHandle("old-console"),
            multimedia: new OpaqueEndpointHandle("old-multimedia"),
            communications: new OpaqueEndpointHandle("communications"));
        var coordinator = new DefaultEndpointSwitchCoordinator(
            policy,
            new StubGenerationFence(current: 7));

        DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
            Candidate("target", new OpaqueContainerHandle("selected"), BluetoothAudioProfile.Stereo),
            generation: 7,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(DefaultEndpointSwitchOutcome.Applied, result.Outcome);
        Assert.Equal(
            [DefaultEndpointRole.Console, DefaultEndpointRole.Multimedia],
            policy.WrittenRoles);
        Assert.DoesNotContain(DefaultEndpointRole.Communications, policy.WrittenRoles);
        Assert.All(result.Roles, role =>
        {
            Assert.True(role.NotificationObserved);
            Assert.True(role.ReadBackMatched);
        });
        Assert.Equal(target, await policy.GetDefaultEndpointAsync(
            DefaultEndpointRole.Console,
            CancellationToken.None));
    }

    [Fact]
    public async Task SecondRoleFailureIsReportedAsHonestPartialState()
    {
        var policy = new StubPolicy(
            console: new OpaqueEndpointHandle("old-console"),
            multimedia: new OpaqueEndpointHandle("old-multimedia"),
            communications: new OpaqueEndpointHandle("communications"))
        {
            RejectedRole = DefaultEndpointRole.Multimedia,
        };
        var coordinator = new DefaultEndpointSwitchCoordinator(
            policy,
            new StubGenerationFence(current: 3));

        DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
            Candidate("target", new OpaqueContainerHandle("selected"), BluetoothAudioProfile.Stereo),
            generation: 3,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.HasAcceptedMutation);
        Assert.Equal(DefaultEndpointSwitchOutcome.Partial, result.Outcome);
        Assert.True(result.CommunicationsUnchanged);
        Assert.Equal(2, result.Roles.Count);
        Assert.True(result.Roles[0].ReadBackMatched);
        Assert.False(result.Roles[1].WriteAccepted);
        Assert.Equal(unchecked((int)0x80004005), result.Roles[1].HResult);
    }

    [Fact]
    public async Task AlreadyDefaultIsIdempotentAndDoesNotWaitForNotifications()
    {
        var target = new OpaqueEndpointHandle("target");
        var policy = new StubPolicy(
            console: target,
            multimedia: target,
            communications: new OpaqueEndpointHandle("communications"));
        var coordinator = new DefaultEndpointSwitchCoordinator(
            policy,
            new StubGenerationFence(current: 11));

        DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
            Candidate("target", new OpaqueContainerHandle("selected"), BluetoothAudioProfile.Stereo),
            generation: 11,
            CancellationToken.None);

        Assert.Equal(DefaultEndpointSwitchOutcome.AlreadyDefault, result.Outcome);
        Assert.Empty(policy.WrittenRoles);
        Assert.Equal(0, policy.NotificationWaits);
    }

    [Fact]
    public async Task SupersededGenerationCannotWriteAnyRole()
    {
        var policy = new StubPolicy(
            console: new OpaqueEndpointHandle("old-console"),
            multimedia: new OpaqueEndpointHandle("old-multimedia"),
            communications: new OpaqueEndpointHandle("communications"));
        var coordinator = new DefaultEndpointSwitchCoordinator(
            policy,
            new StubGenerationFence(current: 9));

        DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
            Candidate("target", new OpaqueContainerHandle("selected"), BluetoothAudioProfile.Stereo),
            generation: 8,
            CancellationToken.None);

        Assert.Equal(DefaultEndpointSwitchOutcome.Superseded, result.Outcome);
        Assert.Empty(policy.WrittenRoles);
        Assert.Equal(0, policy.Reads);
    }

    [Fact]
    public async Task GenerationChangeAfterFirstWriteStopsBeforeSecondRole()
    {
        var fence = new MutableGenerationFence(current: 4);
        var policy = new StubPolicy(
            console: new OpaqueEndpointHandle("old-console"),
            multimedia: new OpaqueEndpointHandle("old-multimedia"),
            communications: new OpaqueEndpointHandle("communications"))
        {
            AfterNotification = () => fence.Current = 5,
        };
        var coordinator = new DefaultEndpointSwitchCoordinator(policy, fence);

        DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
            Candidate("target", new OpaqueContainerHandle("selected"), BluetoothAudioProfile.Stereo),
            generation: 4,
            CancellationToken.None);

        Assert.Equal(DefaultEndpointSwitchOutcome.Superseded, result.Outcome);
        Assert.True(result.HasAcceptedMutation);
        Assert.Equal([DefaultEndpointRole.Console], policy.WrittenRoles);
    }

    private static DefaultEndpointCandidate Candidate(
        string endpoint,
        OpaqueContainerHandle container,
        BluetoothAudioProfile profile) =>
        new(
            new OpaqueEndpointHandle(endpoint),
            container,
            AudioDataFlow.Render,
            profile,
            AudioEndpointAvailability.Active);

    private sealed class StubGenerationFence(long current)
        : IDefaultEndpointGenerationFence
    {
        public bool IsCurrent(long generation) => generation == current;
    }

    private sealed class MutableGenerationFence(long current)
        : IDefaultEndpointGenerationFence
    {
        public long Current { get; set; } = current;

        public bool IsCurrent(long generation) => generation == Current;
    }

    private sealed class StubPolicy(
        OpaqueEndpointHandle? console,
        OpaqueEndpointHandle? multimedia,
        OpaqueEndpointHandle? communications)
        : IDefaultAudioEndpointPolicy
    {
        private readonly Dictionary<DefaultEndpointRole, OpaqueEndpointHandle?> _defaults = new()
        {
            [DefaultEndpointRole.Console] = console,
            [DefaultEndpointRole.Multimedia] = multimedia,
            [DefaultEndpointRole.Communications] = communications,
        };

        public DefaultEndpointRole? RejectedRole { get; init; }

        public Action? AfterNotification { get; init; }

        public List<DefaultEndpointRole> WrittenRoles { get; } = [];

        public int NotificationWaits { get; private set; }

        public int Reads { get; private set; }

        public ValueTask<OpaqueEndpointHandle?> GetDefaultEndpointAsync(
            DefaultEndpointRole role,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return ValueTask.FromResult(_defaults[role]);
        }

        public ValueTask<DefaultEndpointWriteResult> SetDefaultEndpointAsync(
            OpaqueEndpointHandle endpoint,
            DefaultEndpointRole role,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WrittenRoles.Add(role);
            if (RejectedRole == role)
            {
                return ValueTask.FromResult(new DefaultEndpointWriteResult(
                    Accepted: false,
                    HResult: unchecked((int)0x80004005)));
            }

            _defaults[role] = endpoint;
            return ValueTask.FromResult(new DefaultEndpointWriteResult(
                Accepted: true,
                HResult: 0));
        }

        public ValueTask<DefaultEndpointNotification> WaitForDefaultEndpointChangedAsync(
            OpaqueEndpointHandle endpoint,
            DefaultEndpointRole role,
            long generation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NotificationWaits++;
            AfterNotification?.Invoke();
            return ValueTask.FromResult(new DefaultEndpointNotification(
                role,
                generation,
                Observed: true));
        }
    }
}
