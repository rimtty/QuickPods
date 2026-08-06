using System.Collections.Immutable;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Core;

public sealed class StateCoordinator : IDisposable
{
    private readonly IAudioEndpointPort audio;
    private readonly IBluetoothAudioPort bluetooth;
    private readonly IDefaultOutputPort defaultOutput;
    private readonly ITaskbarPresentationPort taskbar;
    private readonly SemaphoreSlim gate = new(1, 1);
    private QuickPodsState state = QuickPodsState.Initial;

    public StateCoordinator(
        IAudioEndpointPort audio,
        IBluetoothAudioPort bluetooth,
        IDefaultOutputPort defaultOutput,
        ITaskbarPresentationPort taskbar)
    {
        this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
        this.bluetooth = bluetooth ?? throw new ArgumentNullException(nameof(bluetooth));
        this.defaultOutput = defaultOutput ?? throw new ArgumentNullException(nameof(defaultOutput));
        this.taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
    }

    public QuickPodsState State => state;

    public void Dispose() => gate.Dispose();

    public async ValueTask<QuickPodsState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AudioState currentAudio = await audio.ReadAsync(cancellationToken).ConfigureAwait(false);
            ImmutableArray<BluetoothDeviceState> devices =
                await bluetooth.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            BluetoothDeviceKey? selected = Contains(devices, state.SelectedDevice) ? state.SelectedDevice : null;

            state = state with
            {
                Audio = currentAudio,
                TaskbarCapability = taskbar.Capability,
                DefaultOutputCapability = defaultOutput.Capability,
                BluetoothDevices = devices,
                SelectedDevice = selected,
                Operation = QuickPodsOperation.None,
                Error = state.SelectedDevice.HasValue && !selected.HasValue
                    ? QuickPodsErrorCode.BluetoothSelectionStale
                    : null,
                Generation = state.Generation + 1,
            };

            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<QuickPodsState> SelectAsync(
        BluetoothDeviceKey device,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Contains(state.BluetoothDevices, device))
            {
                state = state with
                {
                    Error = QuickPodsErrorCode.BluetoothSelectionStale,
                    Generation = state.Generation + 1,
                };
                return state;
            }

            state = state with
            {
                SelectedDevice = device,
                Operation = QuickPodsOperation.None,
                Error = null,
                Generation = state.Generation + 1,
            };
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<QuickPodsState> ConnectSelectedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BluetoothDeviceState? selected = state.FindSelectedDevice();
            if (selected is null)
            {
                return SetError(QuickPodsErrorCode.BluetoothSelectionStale);
            }

            if (selected.Capability != BluetoothDeviceCapability.DirectControl)
            {
                return SetError(QuickPodsErrorCode.BluetoothDriverUnsupported);
            }

            SetSelectedState(
                selected with { ConnectionState = BluetoothConnectionState.Connecting },
                QuickPodsOperation.Connecting,
                null);

            BluetoothConnectionState connection =
                await bluetooth.ConnectAsync(selected.Key, cancellationToken).ConfigureAwait(false);
            if (connection != BluetoothConnectionState.Connected)
            {
                SetSelectedState(
                    selected with { ConnectionState = connection },
                    QuickPodsOperation.None,
                    QuickPodsErrorCode.BluetoothTimeout);
                return state;
            }

            selected = selected with
            {
                ConnectionState = BluetoothConnectionState.Connected,
                DefaultOutputState = DefaultOutputState.NotDefault,
            };

            if (defaultOutput.Capability != DefaultOutputCapability.Supported)
            {
                SetSelectedState(
                    selected,
                    QuickPodsOperation.None,
                    QuickPodsErrorCode.DefaultOutputSwitchFailed);
                return state;
            }

            SetSelectedState(
                selected with { DefaultOutputState = DefaultOutputState.SettingDefault },
                QuickPodsOperation.SettingDefault,
                null);
            bool becameDefault =
                await defaultOutput.MakeDefaultAsync(selected.Key, cancellationToken).ConfigureAwait(false);

            SetSelectedState(
                selected with
                {
                    DefaultOutputState = becameDefault
                        ? DefaultOutputState.Default
                        : DefaultOutputState.Failed,
                },
                QuickPodsOperation.None,
                becameDefault ? null : QuickPodsErrorCode.DefaultOutputSwitchFailed);
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<QuickPodsState> DisconnectSelectedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BluetoothDeviceState? selected = state.FindSelectedDevice();
            if (selected is null)
            {
                return SetError(QuickPodsErrorCode.BluetoothSelectionStale);
            }

            if (selected.Capability != BluetoothDeviceCapability.DirectControl)
            {
                return SetError(QuickPodsErrorCode.BluetoothDriverUnsupported);
            }

            SetSelectedState(
                selected with { ConnectionState = BluetoothConnectionState.Disconnecting },
                QuickPodsOperation.Disconnecting,
                null);
            BluetoothConnectionState connection =
                await bluetooth.DisconnectAsync(selected.Key, cancellationToken).ConfigureAwait(false);

            bool disconnected = connection == BluetoothConnectionState.Disconnected;
            SetSelectedState(
                selected with
                {
                    ConnectionState = connection,
                    DefaultOutputState = disconnected
                        ? DefaultOutputState.NotApplicable
                        : selected.DefaultOutputState,
                },
                QuickPodsOperation.None,
                disconnected ? null : QuickPodsErrorCode.BluetoothTimeout);
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool Contains(
        ImmutableArray<BluetoothDeviceState> devices,
        BluetoothDeviceKey? key)
    {
        if (key is not { } value)
        {
            return false;
        }

        foreach (BluetoothDeviceState device in devices)
        {
            if (device.Key == value)
            {
                return true;
            }
        }

        return false;
    }

    private QuickPodsState SetError(QuickPodsErrorCode error)
    {
        state = state with
        {
            Operation = QuickPodsOperation.None,
            Error = error,
            Generation = state.Generation + 1,
        };
        return state;
    }

    private void SetSelectedState(
        BluetoothDeviceState selected,
        QuickPodsOperation operation,
        QuickPodsErrorCode? error)
    {
        ImmutableArray<BluetoothDeviceState>.Builder devices = state.BluetoothDevices.ToBuilder();
        for (int index = 0; index < devices.Count; index++)
        {
            if (devices[index].Key == selected.Key)
            {
                devices[index] = selected;
                break;
            }
        }

        state = state with
        {
            BluetoothDevices = devices.MoveToImmutable(),
            Operation = operation,
            Error = error,
            Generation = state.Generation + 1,
        };
    }
}
