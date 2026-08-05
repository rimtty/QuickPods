using QuickPods.Spike.BluetoothKs.Discovery;
using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Observation;
using QuickPods.Spike.BluetoothKs.Operations;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal sealed class ProcessKsCommandInvoker(
    string adapterDeviceId,
    IKsChildProcessRunner childRunner) : IBluetoothKsCommandInvoker
{
    public async Task<int> InvokeAsync(BluetoothOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        KsChildOperation operation = request.Kind == BluetoothOperationKind.Connect
            ? KsChildOperation.Reconnect
            : KsChildOperation.Disconnect;
        KsChildRunResult result = await childRunner.RunAsync(
            new KsChildInvocation(
                request.ContainerKey,
                adapterDeviceId,
                operation,
                KsChildConsent.Mutation),
            CancellationToken.None).ConfigureAwait(false);
        if (result.Status == KsChildRunStatus.TimedOut)
        {
            throw new BluetoothKsInvocationTimedOutException();
        }

        if (result.Status != KsChildRunStatus.Completed || result.Response is null)
        {
            throw new KsChildProcessException(result.Status);
        }

        return result.Response.HResult;
    }
}

internal sealed class DiscoveryBluetoothStateObserver(
    IBluetoothKsDiscovery discovery) : IBluetoothStateObserver
{
    public Task<BluetoothStateObservation> ObserveAsync(
        string containerKey,
        long generation,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Observe(containerKey, generation),
            cancellationToken);
    }

    private BluetoothStateObservation Observe(string containerKey, long generation)
    {
        BluetoothKsDiscoveryResult result = discovery.Discover();
        BluetoothDeviceGroup? group = result.Inventory.Groups.SingleOrDefault(
            candidate => string.Equals(
                candidate.ContainerHash,
                containerKey,
                StringComparison.Ordinal));
        bool enumerationComplete =
            !result.Inventory.Faults.Any(fault => fault.AffectsOwnership) &&
            result.UnassignedAdapterDeviceIds.Count == 0;
        IReadOnlyList<BluetoothEndpointState> endpointStates = group?.Endpoints
            .Where(endpoint => endpoint.Flow == NativeDataFlow.Render)
            .Select(endpoint => MapState(endpoint.State))
            .ToArray() ?? [];
        var evidence = new BluetoothContainerEvidence(
            TargetConfigured: true,
            ContainerPresent: group is not null,
            EnumerationComplete: enumerationComplete,
            endpointStates);
        return new BluetoothStateObservation(generation, evidence);
    }

    private static BluetoothEndpointState MapState(uint state)
    {
        if ((state & NativeConstants.DeviceStateActive) != 0)
        {
            return BluetoothEndpointState.Active;
        }

        if ((state & NativeConstants.DeviceStateDisabled) != 0)
        {
            return BluetoothEndpointState.Disabled;
        }

        if ((state & NativeConstants.DeviceStateNotPresent) != 0)
        {
            return BluetoothEndpointState.NotPresent;
        }

        if ((state & NativeConstants.DeviceStateUnplugged) != 0)
        {
            return BluetoothEndpointState.Unplugged;
        }

        return BluetoothEndpointState.Unknown;
    }
}

internal sealed class KsChildProcessException(KsChildRunStatus status) : InvalidOperationException($"The isolated KS child ended with status {status}.")
{
    public KsChildRunStatus Status { get; } = status;
}
