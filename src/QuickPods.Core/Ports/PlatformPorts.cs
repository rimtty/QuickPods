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

public interface IBluetoothAudioCatalogPort
{
    ValueTask<BluetoothAudioCatalogObservation> DiscoverAsync(
        long inventoryGeneration,
        CancellationToken cancellationToken);
}

/// <summary>
/// Performs one validated physical-device mutation. Implementations must never retry a submitted
/// OS request. After submission they settle observation and return a result instead of surfacing
/// caller cancellation as proof that the OS request did not occur.
/// </summary>
public interface IBluetoothDeviceOperationPort
{
    ValueTask<BluetoothDeviceOperationResult> ConnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken);

    ValueTask<BluetoothDeviceOperationResult> DisconnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken);
}

/// <summary>
/// Changes and verifies the default render endpoint for an already connected physical device.
/// A failed result must not disconnect the Bluetooth device or restore an unrelated endpoint.
/// </summary>
public interface IDefaultOutputOperationPort
{
    DefaultOutputCapability Capability { get; }

    ValueTask<DefaultOutputOperationResult> MakeDefaultAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken);
}

/// <summary>
/// Serializes the complete Bluetooth operation, including any subsequent default-output write,
/// across QuickPods processes. Implementations must retain ownership until the supplied operation
/// has fully settled and must reject an abandoned owner once before allowing a later operation.
/// </summary>
public interface IBluetoothOperationGatePort
{
    ValueTask<BluetoothOperationAdmissionResult<T>> RunAsync<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken);
}

public interface IBluetoothSelectionStore
{
    ValueTask<BluetoothDeviceKey?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        BluetoothDeviceKey? selectedDevice,
        CancellationToken cancellationToken = default);
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
