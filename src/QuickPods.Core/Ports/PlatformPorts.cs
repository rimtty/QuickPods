using System.Collections.Immutable;
using QuickPods.Core.Models;

namespace QuickPods.Core.Ports;

public interface IAudioEndpointPort
{
    ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken);
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
