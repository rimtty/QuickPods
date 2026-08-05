namespace QuickPods.Spike.BluetoothKs.Observation;

public static class BluetoothStateClassifier
{
    public static BluetoothAudioState Classify(BluetoothContainerEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.EndpointStates);

        if (!evidence.TargetConfigured)
        {
            return BluetoothAudioState.NotConfigured;
        }

        if (!evidence.EnumerationComplete)
        {
            return BluetoothAudioState.Unknown;
        }

        if (!evidence.ContainerPresent)
        {
            return BluetoothAudioState.NotConfigured;
        }

        if (evidence.EndpointStates.Count == 0)
        {
            return BluetoothAudioState.Unknown;
        }

        if (evidence.EndpointStates.Contains(BluetoothEndpointState.Active))
        {
            return BluetoothAudioState.Connected;
        }

        if (evidence.EndpointStates.Contains(BluetoothEndpointState.Unknown))
        {
            return BluetoothAudioState.Unknown;
        }

        if (evidence.EndpointStates.All(state => state == BluetoothEndpointState.Disabled))
        {
            return BluetoothAudioState.Disabled;
        }

        if (evidence.EndpointStates.All(IsDisconnectedState))
        {
            return BluetoothAudioState.Disconnected;
        }

        return BluetoothAudioState.Unknown;
    }

    private static bool IsDisconnectedState(BluetoothEndpointState state)
    {
        return state is BluetoothEndpointState.NotPresent or BluetoothEndpointState.Unplugged;
    }
}
