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
            new Dictionary<string, RawKsTarget>(StringComparer.Ordinal),
            unassignedAdapter ? ["unassigned-adapter"] : []);
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

    private sealed class FakeDiscovery(BluetoothKsDiscoveryResult result)
        : IBluetoothKsDiscovery
    {
        public BluetoothKsDiscoveryResult Discover() => result;
    }
}
