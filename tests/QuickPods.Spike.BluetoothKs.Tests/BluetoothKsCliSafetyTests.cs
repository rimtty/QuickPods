using QuickPods.Spike.BluetoothKs.Discovery;
using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class BluetoothKsCliSafetyTests
{
    private const string SessionToken =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string TargetHash = "A1B2C3D4E5F60123456789AB";

    [Fact]
    public async Task InventoryNeverStartsAKsChildOperation()
    {
        var discovery = new FakeDiscovery(ResultWithTarget(RenderCandidate("adapter-render")));
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            BluetoothKsOptions.Parse(["inventory"]),
            discovery,
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, discovery.Calls);
        Assert.Empty(childRunner.Calls);
    }

    [Fact]
    public async Task UnknownTargetIsRejectedBeforeStartingAKsChildOperation()
    {
        var discovery = new FakeDiscovery(EmptyResult());
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            discovery,
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(childRunner.Calls);
        Assert.Contains("was not found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleRenderKsCandidatesAreRejectedBeforeStartingAChildOperation()
    {
        BluetoothKsDiscoveryResult result = ResultWithTarget(
            RenderCandidate("adapter-render-a"),
            RenderCandidate("adapter-render-b"),
            new RawKsCandidate("adapter-capture", NativeDataFlow.Capture));
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(result),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(childRunner.Calls);
        Assert.Contains("2 render KS candidates", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KsCandidateSharedAcrossContainersIsRejectedBeforeAChildOperation()
    {
        const string sharedAdapter = "adapter-shared";
        const string otherTargetHash = "00112233445566778899AABB";
        var selected = new RawKsTarget(
            TargetHash,
            [RenderCandidate(sharedAdapter)]);
        var other = new RawKsTarget(
            otherTargetHash,
            [new RawKsCandidate(sharedAdapter, NativeDataFlow.Capture)]);
        var result = new BluetoothKsDiscoveryResult(
            EmptyInventory(),
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal)
            {
                [TargetHash] = selected,
                [otherTargetHash] = other,
            },
            []);
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(result),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(childRunner.Calls);
        Assert.Contains("shared by 2 containers", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("connect", "BasicSupportDisconnect")]
    [InlineData("disconnect", "BasicSupportReconnect")]
    public async Task UnsupportedBasicSupportPreventsReconnectAndDisconnect(
        string command,
        string unsupportedProbeName)
    {
        KsChildOperation unsupportedProbe = Enum.Parse<KsChildOperation>(unsupportedProbeName);
        var childRunner = new FakeChildRunner(operation =>
        {
            if (operation is KsChildOperation.Reconnect or KsChildOperation.Disconnect)
            {
                throw new InvalidOperationException("A mutating KS child operation must not run.");
            }

            return operation == unsupportedProbe ? Unsupported() : Supported();
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            MutationOptions(command),
            new FakeDiscovery(ResultWithTarget(RenderCandidate("adapter-render"))),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            [KsChildOperation.BasicSupportReconnect, KsChildOperation.BasicSupportDisconnect],
            childRunner.Calls.Select(call => call.Operation));
        Assert.DoesNotContain(
            childRunner.Calls,
            call => call.Operation is KsChildOperation.Reconnect or KsChildOperation.Disconnect);
        Assert.Contains("Mutation refused", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeRunsExactlyTheTwoBasicSupportOperations()
    {
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(ResultWithTarget(RenderCandidate("adapter-render"))),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Collection(
            childRunner.Calls,
            call =>
            {
                Assert.Equal("adapter-render", call.Invocation.AdapterDeviceId);
                Assert.Equal(TargetHash, call.Invocation.TargetHash);
                Assert.Equal(KsChildOperation.BasicSupportReconnect, call.Operation);
            },
            call =>
            {
                Assert.Equal("adapter-render", call.Invocation.AdapterDeviceId);
                Assert.Equal(TargetHash, call.Invocation.TargetHash);
                Assert.Equal(KsChildOperation.BasicSupportDisconnect, call.Operation);
            });
    }

    [Fact]
    public async Task ChildTimeoutStopsBeforeASecondDriverQuery()
    {
        var childRunner = new FakeChildRunner(_ => new KsChildRunResult(
            KsChildRunStatus.TimedOut,
            Response: null,
            ExitCode: 1));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(ResultWithTarget(RenderCandidate("adapter-render"))),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Single(childRunner.Calls);
        Assert.Equal(
            KsChildOperation.BasicSupportReconnect,
            childRunner.Calls[0].Operation);
        Assert.Contains("was not attempted", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnershipAffectingDiscoveryFaultRejectsBeforeStartingAChildOperation()
    {
        BluetoothKsDiscoveryResult complete = ResultWithTarget(
            RenderCandidate("adapter-render"));
        var result = complete with
        {
            Inventory = complete.Inventory with
            {
                Faults =
                [
                    new DiscoveryFault(
                        "IDeviceTopology.GetConnector",
                        unchecked((int)0x80004005),
                        AffectsOwnership: true),
                ],
            },
        };
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(result),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(childRunner.Calls);
        Assert.Contains("ownership proof is incomplete", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedAdapterWithoutContainerOwnershipIsRejectedBeforeStartingAChildOperation()
    {
        BluetoothKsDiscoveryResult complete = ResultWithTarget(
            RenderCandidate("adapter-render"));
        BluetoothKsDiscoveryResult result = complete with
        {
            UnassignedAdapterDeviceIds = ["adapter-render"],
        };
        var childRunner = new FakeChildRunner(_ => Supported());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await BluetoothKsCli.ExecuteAsync(
            ProbeOptions(),
            new FakeDiscovery(result),
            childRunner,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(childRunner.Calls);
        Assert.Contains("Container ownership is incomplete", error.ToString(), StringComparison.Ordinal);
    }

    private static BluetoothKsOptions ProbeOptions()
    {
        return BluetoothKsOptions.Parse(
            [
                "probe",
                "--session",
                SessionToken,
                "--target",
                TargetHash,
                "--confirm-ks-operation",
            ]);
    }

    private static BluetoothKsOptions MutationOptions(string command)
    {
        return BluetoothKsOptions.Parse(
        [
            command,
            "--session",
            SessionToken,
            "--target",
            TargetHash,
            "--confirm-target",
            TargetHash,
            "--confirm-ks-operation",
            "--confirm-playback-stopped",
        ]);
    }

    private static RawKsCandidate RenderCandidate(string adapterDeviceId)
    {
        return new RawKsCandidate(
            adapterDeviceId,
            NativeDataFlow.Render);
    }

    private static BluetoothKsDiscoveryResult ResultWithTarget(params RawKsCandidate[] candidates)
    {
        var target = new RawKsTarget(
            TargetHash,
            candidates);
        return new BluetoothKsDiscoveryResult(
            EmptyInventory(),
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal)
            {
                [TargetHash] = target,
            },
            []);
    }

    private static BluetoothKsDiscoveryResult EmptyResult()
    {
        return new BluetoothKsDiscoveryResult(
            EmptyInventory(),
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal),
            []);
    }

    private static BluetoothKsInventory EmptyInventory()
    {
        return new BluetoothKsInventory(SessionToken, [], []);
    }

    private static KsChildRunResult Supported()
    {
        return new KsChildRunResult(
            KsChildRunStatus.Completed,
            new KsChildResponse(
                HResult: 0,
                BytesReturned: sizeof(uint),
                SupportFlags: KernelStreamingContracts.PropertyTypeGet),
            ExitCode: 0);
    }

    private static KsChildRunResult Unsupported()
    {
        return new KsChildRunResult(
            KsChildRunStatus.Completed,
            new KsChildResponse(
                HResult: 0,
                BytesReturned: sizeof(uint),
                SupportFlags: 0),
            ExitCode: 0);
    }

    private sealed class FakeDiscovery(BluetoothKsDiscoveryResult result) : IBluetoothKsDiscovery
    {
        public int Calls { get; private set; }

        public BluetoothKsDiscoveryResult Discover()
        {
            Calls++;
            return result;
        }
    }

    private sealed class FakeChildRunner(Func<KsChildOperation, KsChildRunResult> run)
        : IKsChildProcessRunner
    {
        public List<ChildCall> Calls { get; } = [];

        public Task<KsChildRunResult> RunAsync(
            KsChildInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new ChildCall(invocation));
            return Task.FromResult(run(invocation.Operation));
        }
    }

    private sealed record ChildCall(KsChildInvocation Invocation)
    {
        public KsChildOperation Operation => Invocation.Operation;
    }
}
