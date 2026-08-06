using QuickPods.Spike.BluetoothKs.Discovery;
using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Observation;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class DiscoveryBluetoothStateObserverTests
{
    private const string TargetHash = "A1B2C3D4E5F60123456789AB";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IncompleteOwnershipEvidenceCanNeverReportDisconnected(
        bool ownershipAffectingFault,
        bool unassignedAdapter)
    {
        DiscoveryFault[] faults = ownershipAffectingFault
            ?
            [
                new DiscoveryFault(
                    "IDeviceTopology.GetConnector",
                    unchecked((int)0x80004005),
                    AffectsOwnership: true),
            ]
            : [];
        var inventory = new BluetoothKsInventory(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            [
                new BluetoothDeviceGroup(
                    TargetHash,
                    "Bluetooth audio",
                    [
                        new SanitizedAudioEndpoint(
                            "00112233445566778899AABB",
                            "Bluetooth audio",
                            NativeDataFlow.Render,
                            NativeConstants.DeviceStateUnplugged),
                    ],
                    []),
            ],
            faults);
        var result = new BluetoothKsDiscoveryResult(
            inventory,
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal)
            {
                [TargetHash] = new RawKsTarget(
                    TargetHash,
                    [new RawKsCandidate("target-adapter", NativeDataFlow.Render)]),
            },
            unassignedAdapter ? ["target-adapter"] : []);
        var observer = new DiscoveryBluetoothStateObserver(new FakeDiscovery(result));

        BluetoothStateObservation observation = await observer.ObserveAsync(
            TargetHash,
            generation: 7,
            CancellationToken.None);

        Assert.False(observation.Evidence.EnumerationComplete);
        Assert.Equal(
            BluetoothAudioState.Unknown,
            BluetoothStateClassifier.Classify(observation.Evidence));
    }

    [Fact]
    public async Task UnrelatedIncompleteEvidenceDoesNotHideDisconnectedTargetState()
    {
        var inventory = new BluetoothKsInventory(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            [
                new BluetoothDeviceGroup(
                    TargetHash,
                    "Bluetooth audio",
                    [
                        new SanitizedAudioEndpoint(
                            "00112233445566778899AABB",
                            "Bluetooth audio",
                            NativeDataFlow.Render,
                            NativeConstants.DeviceStateUnplugged),
                    ],
                    []),
            ],
            [
                new DiscoveryFault(
                    "IDeviceTopology",
                    unchecked((int)0x80004002),
                    AffectsOwnership: true,
                    EndpointHash: "FFEEDDCCBBAA998877665544",
                    ContainerHash: "11223344556677889900AABB"),
            ]);
        var result = new BluetoothKsDiscoveryResult(
            inventory,
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal)
            {
                [TargetHash] = new RawKsTarget(
                    TargetHash,
                    [new RawKsCandidate("target-adapter", NativeDataFlow.Render)]),
            },
            ["unrelated-adapter"]);
        var observer = new DiscoveryBluetoothStateObserver(new FakeDiscovery(result));

        BluetoothStateObservation observation = await observer.ObserveAsync(
            TargetHash,
            generation: 8,
            CancellationToken.None);

        Assert.True(observation.Evidence.EnumerationComplete);
        Assert.Equal(
            BluetoothAudioState.Disconnected,
            BluetoothStateClassifier.Classify(observation.Evidence));
    }

    [Fact]
    public async Task FullDisconnectObservationIncludesCaptureEndpoints()
    {
        var inventory = new BluetoothKsInventory(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            [
                new BluetoothDeviceGroup(
                    TargetHash,
                    "AirPods audio",
                    [
                        new SanitizedAudioEndpoint(
                            "00112233445566778899AABB",
                            "AirPods audio",
                            NativeDataFlow.Render,
                            NativeConstants.DeviceStateUnplugged),
                        new SanitizedAudioEndpoint(
                            "112233445566778899AABBCC",
                            "AirPods audio",
                            NativeDataFlow.Capture,
                            NativeConstants.DeviceStateActive),
                    ],
                    []),
            ],
            []);
        var result = new BluetoothKsDiscoveryResult(
            inventory,
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal)
            {
                [TargetHash] = new RawKsTarget(
                    TargetHash,
                    [
                        new RawKsCandidate("render-adapter", NativeDataFlow.Render),
                        new RawKsCandidate("capture-adapter", NativeDataFlow.Capture),
                    ]),
            },
            []);
        var observer = new DiscoveryBluetoothStateObserver(
            new FakeDiscovery(result),
            includeCapture: true);

        BluetoothStateObservation observation = await observer.ObserveAsync(
            TargetHash,
            generation: 9,
            CancellationToken.None);

        Assert.Equal(
            BluetoothAudioState.Connected,
            BluetoothStateClassifier.Classify(observation.Evidence));
    }

    private sealed class FakeDiscovery(BluetoothKsDiscoveryResult result)
        : IBluetoothKsDiscovery
    {
        public BluetoothKsDiscoveryResult Discover() => result;
    }
}
