using System.Collections.Immutable;
using QuickPods.Core.Models;

namespace QuickPods.Windows.Bluetooth;

internal sealed record WindowsBluetoothEndpointBinding(
    string EndpointId,
    BluetoothEndpointDirection Direction,
    BluetoothAudioProfile Profile,
    BluetoothEndpointAvailability Availability,
    ImmutableArray<string> AdapterDeviceIds);

internal sealed record WindowsBluetoothDeviceBinding(
    BluetoothDeviceKey DeviceKey,
    Guid ContainerId,
    ImmutableArray<WindowsBluetoothEndpointBinding> Endpoints,
    bool HasAmbiguousAdapterOwnership);

internal sealed class WindowsBluetoothBindingRegistry
{
    private readonly object sync = new();
    private long inventoryGeneration = -1;
    private Dictionary<BluetoothDeviceKey, WindowsBluetoothDeviceBinding> bindings = [];

    internal bool Publish(
        long generation,
        IEnumerable<WindowsBluetoothDeviceBinding> discoveredBindings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentNullException.ThrowIfNull(discoveredBindings);
        Dictionary<BluetoothDeviceKey, WindowsBluetoothDeviceBinding> candidate = discoveredBindings
            .ToDictionary(binding => binding.DeviceKey);
        lock (sync)
        {
            if (generation <= inventoryGeneration)
            {
                return false;
            }

            inventoryGeneration = generation;
            bindings = candidate;
            return true;
        }
    }

    internal bool TryResolve(
        BluetoothOperationTarget target,
        out WindowsBluetoothDeviceBinding binding)
    {
        lock (sync)
        {
            if (target.InventoryGeneration == inventoryGeneration &&
                bindings.TryGetValue(target.DeviceKey, out WindowsBluetoothDeviceBinding? resolved))
            {
                binding = resolved;
                return true;
            }
        }

        binding = null!;
        return false;
    }
}
