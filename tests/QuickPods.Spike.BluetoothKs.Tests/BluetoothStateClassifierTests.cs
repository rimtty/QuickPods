using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class BluetoothStateClassifierTests
{
    [Fact]
    public void AnyActiveEndpointIsConnected()
    {
        BluetoothAudioState state = BluetoothStateClassifier.Classify(Evidence(
            BluetoothEndpointState.Unknown,
            BluetoothEndpointState.Active));

        Assert.Equal(BluetoothAudioState.Connected, state);
    }

    [Fact]
    public void AllDisabledEndpointsAreDisabled()
    {
        BluetoothAudioState state = BluetoothStateClassifier.Classify(Evidence(
            BluetoothEndpointState.Disabled,
            BluetoothEndpointState.Disabled));

        Assert.Equal(BluetoothAudioState.Disabled, state);
    }

    [Fact]
    public void MissingConfigurationIsNotConfigured()
    {
        var evidence = new BluetoothContainerEvidence(
            TargetConfigured: false,
            ContainerPresent: false,
            EnumerationComplete: true,
            EndpointStates: []);

        Assert.Equal(BluetoothAudioState.NotConfigured, BluetoothStateClassifier.Classify(evidence));
    }

    [Fact]
    public void IncompleteOrContradictoryEvidenceIsUnknown()
    {
        var incomplete = new BluetoothContainerEvidence(
            TargetConfigured: true,
            ContainerPresent: true,
            EnumerationComplete: false,
            EndpointStates: [BluetoothEndpointState.Active]);
        BluetoothContainerEvidence contradictory = Evidence(
            BluetoothEndpointState.Disabled,
            BluetoothEndpointState.Unplugged);

        Assert.Equal(BluetoothAudioState.Unknown, BluetoothStateClassifier.Classify(incomplete));
        Assert.Equal(BluetoothAudioState.Unknown, BluetoothStateClassifier.Classify(contradictory));
    }

    [Fact]
    public void UnpluggedAndNotPresentEndpointsAreDisconnected()
    {
        BluetoothAudioState state = BluetoothStateClassifier.Classify(Evidence(
            BluetoothEndpointState.Unplugged,
            BluetoothEndpointState.NotPresent));

        Assert.Equal(BluetoothAudioState.Disconnected, state);
    }

    private static BluetoothContainerEvidence Evidence(params BluetoothEndpointState[] states)
    {
        return new BluetoothContainerEvidence(
            TargetConfigured: true,
            ContainerPresent: true,
            EnumerationComplete: true,
            EndpointStates: states);
    }
}
