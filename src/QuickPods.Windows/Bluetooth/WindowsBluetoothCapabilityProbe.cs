using QuickPods.Core.Models;
using QuickPods.Windows.Bluetooth.Worker;

namespace QuickPods.Windows.Bluetooth;

internal sealed class WindowsBluetoothCapabilityProbe
{
    private const uint PropertyTypeGet = 0x00000001;

    private readonly BluetoothWorkerProcessRunner worker;

    internal WindowsBluetoothCapabilityProbe(string workerPath)
    {
        worker = new BluetoothWorkerProcessRunner(workerPath);
    }

    internal async Task<BluetoothDeviceCapability> ProbeAsync(
        WindowsBluetoothDeviceBinding binding,
        CancellationToken cancellationToken)
    {
        if (binding.HasAmbiguousAdapterOwnership ||
            binding.Endpoints.IsDefaultOrEmpty ||
            binding.Endpoints.Any(endpoint => endpoint.AdapterDeviceIds.IsDefaultOrEmpty))
        {
            return BluetoothDeviceCapability.OwnershipUnknown;
        }

        string[] reconnectCandidates = [.. binding.Endpoints
            .Where(endpoint =>
                endpoint.Direction == BluetoothEndpointDirection.Render &&
                endpoint.Profile == BluetoothAudioProfile.Stereo)
            .SelectMany(endpoint => endpoint.AdapterDeviceIds)
            .Distinct(StringComparer.Ordinal)];
        if (reconnectCandidates.Length != 1)
        {
            return BluetoothDeviceCapability.OwnershipUnknown;
        }

        string[] disconnectCandidates = [.. binding.Endpoints
            .SelectMany(endpoint => endpoint.AdapterDeviceIds)
            .Distinct(StringComparer.Ordinal)];
        ProbeOutcome reconnect = await ProbeCandidateAsync(
            binding.DeviceKey,
            reconnectCandidates[0],
            BluetoothWorkerOperation.BasicSupportReconnect,
            cancellationToken).ConfigureAwait(false);
        if (reconnect != ProbeOutcome.Supported)
        {
            return MapOutcome(reconnect);
        }

        foreach (string candidate in disconnectCandidates)
        {
            ProbeOutcome disconnect = await ProbeCandidateAsync(
                binding.DeviceKey,
                candidate,
                BluetoothWorkerOperation.BasicSupportDisconnect,
                cancellationToken).ConfigureAwait(false);
            if (disconnect != ProbeOutcome.Supported)
            {
                return MapOutcome(disconnect);
            }
        }

        return BluetoothDeviceCapability.DirectControl;
    }

    private async Task<ProbeOutcome> ProbeCandidateAsync(
        BluetoothDeviceKey targetKey,
        string adapterDeviceId,
        BluetoothWorkerOperation operation,
        CancellationToken cancellationToken)
    {
        BluetoothWorkerRunResult result;
        try
        {
            result = await worker.RunAsync(
                targetKey,
                adapterDeviceId,
                operation,
                mutationConfirmed: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ProbeOutcome.TemporarilyUnavailable;
        }

        if (result.Status != BluetoothWorkerRunStatus.Completed || result.Response is null)
        {
            return ProbeOutcome.TemporarilyUnavailable;
        }

        return result.Response.HResult == 0 &&
            result.Response.SupportFlags is uint flags &&
            (flags & PropertyTypeGet) != 0
                ? ProbeOutcome.Supported
                : ProbeOutcome.Unsupported;
    }

    private static BluetoothDeviceCapability MapOutcome(ProbeOutcome outcome) =>
        outcome == ProbeOutcome.Unsupported
            ? BluetoothDeviceCapability.SettingsOnly
            : BluetoothDeviceCapability.TemporarilyUnavailable;

    private enum ProbeOutcome
    {
        Supported,
        Unsupported,
        TemporarilyUnavailable,
    }
}
