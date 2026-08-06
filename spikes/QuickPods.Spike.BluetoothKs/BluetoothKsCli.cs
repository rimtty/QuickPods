using System.Globalization;
using System.Runtime.InteropServices;
using QuickPods.Spike.BluetoothKs.Discovery;
using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Operations;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs;

internal static class BluetoothKsCli
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        BluetoothKsOptions? options = ParseOptions(arguments, error);
        if (options is null)
        {
            return 2;
        }

        if (options.Command == BluetoothKsCommand.Help)
        {
            WriteHelp(output);
            return 0;
        }

        try
        {
            var runner = new IsolatedCommandProcessRunner();
            CrossProcessKsGateResult<IsolatedCommandRunResult> gated =
                await CrossProcessKsGate.RunAsync(
                    () => runner.RunAsync(
                        arguments,
                        GetOuterTimeout(options.Command),
                        cancellationToken).GetAwaiter().GetResult(),
                    cancellationToken).ConfigureAwait(false);
            if (gated.Status != CrossProcessKsGateStatus.Executed)
            {
                error.WriteLine(
                    "Another Bluetooth diagnostic is active or its previous owner ended unexpectedly; no isolated command was started.");
                return 1;
            }

            IsolatedCommandRunResult result = gated.Value;
            if (result.Status == IsolatedCommandStatus.Completed && result.Response is not null)
            {
                output.Write(result.Response.StandardOutput);
                error.Write(result.Response.StandardError);
                return result.Response.ExitCode;
            }

            error.WriteLine(result.Status == IsolatedCommandStatus.TimedOut
                ? "Isolated diagnostic reached its overall deadline; the process tree was terminated."
                : "Isolated diagnostic failed; no result was accepted.");
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled; no automatic retry was attempted.");
            return 130;
        }
        catch
        {
            error.WriteLine("Diagnostic failed before a sanitized result could be produced.");
            return 1;
        }
    }

    internal static async Task<int> ExecuteLocalAsync(
        BluetoothKsOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            using var discovery = new MtaBluetoothKsDiscovery(options.SessionToken);
            var childRunner = new KsChildProcessRunner();
            return await ExecuteAsync(
                options,
                discovery,
                childRunner,
                output,
                error,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled; no automatic retry was attempted.");
            return 130;
        }
        catch (COMException exception)
        {
            error.WriteLine($"Native discovery failed (HRESULT {FormatHResult(exception.HResult)}).");
            return 1;
        }
        catch
        {
            error.WriteLine("Diagnostic failed before a sanitized result could be produced.");
            return 1;
        }
    }

    internal static async Task<int> ExecuteAsync(
        BluetoothKsOptions options,
        IBluetoothKsDiscovery discovery,
        IKsChildProcessRunner childRunner,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(childRunner);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        cancellationToken.ThrowIfCancellationRequested();
        if (options.Command == BluetoothKsCommand.Inventory)
        {
            WriteInventory(discovery.Discover().Inventory, output);
            return 0;
        }

        BluetoothKsDiscoveryResult discoveryResult = discovery.Discover();
        ResolvedTarget? resolvedTarget = ResolveSingleTarget(
            discoveryResult,
            options.TargetHash!,
            error);
        if (resolvedTarget is null)
        {
            return 1;
        }

        SupportProbe reconnectSupport = await ProbeSupportAsync(
            resolvedTarget.Target.ContainerHash,
            resolvedTarget.RenderAdapterDeviceId,
            KsChildOperation.BasicSupportReconnect,
            childRunner,
            cancellationToken).ConfigureAwait(false);
        WriteSupport("reconnect", reconnectSupport, output);
        if (reconnectSupport.ChildStatus != KsChildRunStatus.Completed)
        {
            error.WriteLine(
                "Second Basic Support query was not attempted after an isolated child failure.");
            return 1;
        }

        var disconnectSupport = new List<SupportProbe>();
        foreach (ResolvedKsCandidate candidate in resolvedTarget.DisconnectCandidates)
        {
            SupportProbe support = await ProbeSupportAsync(
                resolvedTarget.Target.ContainerHash,
                candidate.AdapterDeviceId,
                KsChildOperation.BasicSupportDisconnect,
                childRunner,
                cancellationToken).ConfigureAwait(false);
            disconnectSupport.Add(support);
            WriteSupport(
                $"disconnect-{candidate.Flow.ToString().ToLowerInvariant()}",
                support,
                output);
            if (support.ChildStatus != KsChildRunStatus.Completed)
            {
                error.WriteLine(
                    "A later Basic Support query was not attempted after an isolated child failure.");
                return 1;
            }
        }

        if (options.Command == BluetoothKsCommand.Probe)
        {
            return reconnectSupport.Supported && disconnectSupport.All(item => item.Supported)
                ? 0
                : 1;
        }

        if (!reconnectSupport.Supported || disconnectSupport.Any(item => !item.Supported))
        {
            error.WriteLine(
                "Mutation refused: reconnect and every target-owned disconnect candidate must be supported.");
            return 1;
        }

        BluetoothOperationKind operationKind = options.Command == BluetoothKsCommand.Connect
            ? BluetoothOperationKind.Connect
            : BluetoothOperationKind.Disconnect;
        string[] operationAdapters = operationKind == BluetoothOperationKind.Connect
            ? [resolvedTarget.RenderAdapterDeviceId]
            : [.. resolvedTarget.DisconnectCandidates
                .Select(candidate => candidate.AdapterDeviceId)
                .Distinct(StringComparer.Ordinal)];
        var commandInvoker = new ProcessKsCommandInvoker(
            operationAdapters,
            childRunner);
        var stateObserver = new DiscoveryBluetoothStateObserver(
            discovery,
            includeCapture: operationKind == BluetoothOperationKind.Disconnect);
        var coordinator = new BluetoothOperationCoordinator(commandInvoker, stateObserver);
        BluetoothOperationResult result = await coordinator.ExecuteAsync(
            resolvedTarget.Target.ContainerHash,
            operationKind,
            cancellationToken).ConfigureAwait(false);
        WriteOperation(result, output);
        return result.Succeeded ? 0 : 1;
    }

    private static BluetoothKsOptions? ParseOptions(
        IReadOnlyList<string> arguments,
        TextWriter error)
    {
        try
        {
            return BluetoothKsOptions.Parse(arguments);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"Argument error: {exception.Message}");
            WriteHelp(error);
            return null;
        }
    }

    private static ResolvedTarget? ResolveSingleTarget(
        BluetoothKsDiscoveryResult result,
        string targetHash,
        TextWriter error)
    {
        if (!result.Targets.TryGetValue(targetHash, out RawKsTarget? target))
        {
            error.WriteLine($"Target {targetHash} was not found in the current sanitized inventory.");
            return null;
        }

        TargetOwnershipStatus ownership = BluetoothKsOwnershipVerifier.Evaluate(
            result,
            targetHash);
        if (ownership.HasRelevantFault)
        {
            error.WriteLine(
                $"KS ownership proof is incomplete for target {targetHash}; refusing the selected operation.");
            return null;
        }

        if (ownership.HasUnassignedCandidateOverlap)
        {
            error.WriteLine(
                $"Target {targetHash} uses a KS candidate whose Container ownership is incomplete; refusing to guess.");
            return null;
        }

        string[] renderCandidates = [.. target.Candidates
            .Where(candidate => candidate.SourceFlow == NativeDataFlow.Render)
            .Select(candidate => candidate.AdapterDeviceId)
            .Distinct(StringComparer.Ordinal)];
        if (renderCandidates.Length != 1)
        {
            error.WriteLine(
                $"Target {targetHash} has {renderCandidates.Length.ToString(CultureInfo.InvariantCulture)} render KS candidates; refusing to guess.");
            return null;
        }

        RawKsCandidate[] disconnectCandidates = [.. target.Candidates
            .Where(candidate => candidate.SourceFlow is NativeDataFlow.Render or NativeDataFlow.Capture)
            .Distinct(new RawKsCandidateIdentityComparer())];
        foreach (RawKsCandidate candidate in disconnectCandidates)
        {
            int owningContainers = result.Targets.Values
                .Where(candidateTarget => candidateTarget.Candidates.Any(other =>
                    StringComparer.Ordinal.Equals(
                        other.AdapterDeviceId,
                        candidate.AdapterDeviceId)))
                .Select(candidateTarget => candidateTarget.ContainerHash)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (owningContainers != 1)
            {
                error.WriteLine(
                    $"Target {targetHash} uses a KS candidate shared by {owningContainers.ToString(CultureInfo.InvariantCulture)} containers; refusing ambiguous ownership.");
                return null;
            }
        }

        return new ResolvedTarget(
            target,
            renderCandidates[0],
            [.. disconnectCandidates.Select(candidate => new ResolvedKsCandidate(
                candidate.AdapterDeviceId,
                candidate.SourceFlow))]);
    }

    private static async Task<SupportProbe> ProbeSupportAsync(
        string targetHash,
        string adapterDeviceId,
        KsChildOperation operation,
        IKsChildProcessRunner childRunner,
        CancellationToken cancellationToken)
    {
        KsChildRunResult result = await childRunner.RunAsync(
            new KsChildInvocation(
                targetHash,
                adapterDeviceId,
                operation,
                KsChildConsent.Probe),
            cancellationToken).ConfigureAwait(false);
        bool supported = result.Status == KsChildRunStatus.Completed &&
            result.Response is
            {
                HResult: 0,
                SupportFlags: not null,
            } response &&
            (response.SupportFlags.Value & KernelStreamingContracts.PropertyTypeGet) != 0;
        return new SupportProbe(result.Status, result.Response, supported);
    }

    private static void WriteInventory(BluetoothKsInventory inventory, TextWriter output)
    {
        output.WriteLine($"session={inventory.SessionToken}");
        output.WriteLine(
            $"Sanitized inventory: {inventory.Groups.Count.ToString(CultureInfo.InvariantCulture)} container(s)");
        foreach (BluetoothDeviceGroup group in inventory.Groups)
        {
            output.WriteLine(
                $"target={group.ContainerHash} label={group.Label} endpoints={group.Endpoints.Count.ToString(CultureInfo.InvariantCulture)} filters={group.FilterCandidates.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (SanitizedAudioEndpoint endpoint in group.Endpoints)
            {
                output.WriteLine(
                    $"  endpoint={endpoint.EndpointHash} flow={endpoint.Flow} state={FormatDeviceState(endpoint.State)} label={endpoint.Label}");
            }

            foreach (SanitizedKsFilterCandidate filter in group.FilterCandidates)
            {
                output.WriteLine(
                    $"  filter={filter.FilterHash} source={filter.SourceEndpointHash} flow={filter.SourceFlow} probe={filter.ProbeStatus}");
            }
        }

        foreach (DiscoveryFault fault in inventory.Faults)
        {
            string scope = fault.ContainerHash is not null
                ? $"container:{fault.ContainerHash}"
                : fault.EndpointHash is not null
                    ? $"endpoint:{fault.EndpointHash}"
                    : "global";
            output.WriteLine(
                $"fault={fault.Operation} hresult={FormatHResult(fault.HResult)} ownership-impact={fault.AffectsOwnership.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} scope={scope}");
        }
    }

    private static void WriteSupport(
        string property,
        SupportProbe probe,
        TextWriter output)
    {
        string hResult = probe.Response is null
            ? "none"
            : FormatHResult(probe.Response.HResult);
        string flags = probe.Response?.SupportFlags is uint supportFlags
            ? $"0x{supportFlags:X8}"
            : "none";
        string classification = probe.Supported ? "supported" : "not-supported";
        output.WriteLine(
            $"basic-support={property} classification={classification} child={probe.ChildStatus} hresult={hResult} flags={flags}");
    }

    private static void WriteOperation(BluetoothOperationResult result, TextWriter output)
    {
        string hResult = result.KsHResult is int value
            ? FormatHResult(value)
            : "none";
        output.WriteLine(
            $"operation={result.Request.Kind.ToString().ToLowerInvariant()} outcome={result.Outcome} actual-state={result.ActualState} ks={result.KsCallStatus} hresult={hResult} issued={result.KsRequestIssued.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} elapsed-ms={result.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}");
    }

    private static string FormatDeviceState(uint state)
    {
        return state switch
        {
            NativeConstants.DeviceStateActive => "Active",
            NativeConstants.DeviceStateDisabled => "Disabled",
            NativeConstants.DeviceStateNotPresent => "NotPresent",
            NativeConstants.DeviceStateUnplugged => "Unplugged",
            _ => "Unknown",
        };
    }

    private static string FormatHResult(int hResult) =>
        $"0x{unchecked((uint)hResult):X8}";

    private static TimeSpan GetOuterTimeout(BluetoothKsCommand command)
    {
        return command switch
        {
            BluetoothKsCommand.Inventory => TimeSpan.FromSeconds(10),
            BluetoothKsCommand.Probe => TimeSpan.FromSeconds(15),
            BluetoothKsCommand.Connect or BluetoothKsCommand.Disconnect =>
                TimeSpan.FromSeconds(30),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("QuickPods Bluetooth KS feasibility spike");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  inventory");
        output.WriteLine("      Discover sanitized Container, endpoint, topology, and KS-filter candidates without calling KsProperty.");
        output.WriteLine("  probe --session TOKEN --target ALIAS --confirm-ks-operation");
        output.WriteLine("      Query Basic Support in watchdog-protected child processes without changing connection state.");
        output.WriteLine("  connect|disconnect --session TOKEN --target ALIAS --confirm-target ALIAS --confirm-ks-operation --confirm-playback-stopped");
        output.WriteLine("      Operate exactly one confirmed target and require independent MMDevice state verification.");
        output.WriteLine();
        output.WriteLine("Inventory aliases are scoped by the random session token; full device identifiers and MAC addresses are never printed or persisted.");
    }

    private sealed record SupportProbe(
        KsChildRunStatus ChildStatus,
        KsChildResponse? Response,
        bool Supported);

    private sealed record ResolvedTarget(
        RawKsTarget Target,
        string RenderAdapterDeviceId,
        IReadOnlyList<ResolvedKsCandidate> DisconnectCandidates);

    private sealed record ResolvedKsCandidate(
        string AdapterDeviceId,
        NativeDataFlow Flow);

    private sealed class RawKsCandidateIdentityComparer : IEqualityComparer<RawKsCandidate>
    {
        public bool Equals(RawKsCandidate? left, RawKsCandidate? right) =>
            ReferenceEquals(left, right) ||
            left is not null &&
            right is not null &&
            left.SourceFlow == right.SourceFlow &&
            StringComparer.Ordinal.Equals(left.AdapterDeviceId, right.AdapterDeviceId);

        public int GetHashCode(RawKsCandidate value) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(value.AdapterDeviceId),
            value.SourceFlow);
    }
}
