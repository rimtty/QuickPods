using System.Collections.Immutable;
using QuickPods.Core.Models;

namespace QuickPods.Core.Ports;

public interface IAudioEndpointPort
{
    event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken);

    ValueTask<AudioState> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken);

    ValueTask<AudioState> SetMuteAsync(bool isMuted, CancellationToken cancellationToken);
}

public sealed class AudioStateChangedEventArgs : EventArgs
{
    public AudioStateChangedEventArgs(AudioState state, bool isSelfOriginated)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        IsSelfOriginated = isSelfOriginated;
    }

    public AudioState State { get; }

    public bool IsSelfOriginated { get; }
}

public interface IBluetoothAudioPort
{
    ValueTask<ImmutableArray<BluetoothDeviceState>> GetDevicesAsync(CancellationToken cancellationToken);

    ValueTask<BluetoothConnectionState> ConnectAsync(
        BluetoothDeviceKey device,
        CancellationToken cancellationToken);

    ValueTask<BluetoothConnectionState> DisconnectAsync(
        BluetoothDeviceKey device,
        CancellationToken cancellationToken);
}

public interface IDefaultOutputPort
{
    DefaultOutputCapability Capability { get; }

    ValueTask<bool> MakeDefaultAsync(
        BluetoothDeviceKey device,
        CancellationToken cancellationToken);
}

public interface ITaskbarPresentationPort
{
    TaskbarCapability Capability { get; }
}
