using System.Globalization;
using QuickPods.Spike.DefaultEndpointPolicy.Interop;

namespace QuickPods.Spike.DefaultEndpointPolicy;

internal static class DefaultEndpointPolicyCli
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        DefaultEndpointPolicyOptions options;
        try
        {
            options = DefaultEndpointPolicyOptions.Parse(arguments);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"Argument error: {exception.Message}");
            WriteHelp(error);
            return 2;
        }

        if (options.Command == DefaultEndpointPolicyCommand.Help)
        {
            WriteHelp(output);
            return 0;
        }

        if (options.Command == DefaultEndpointPolicyCommand.Probe)
        {
            return await ProbeAsync(output, error, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            EndpointAliasSession aliases = options.Command == DefaultEndpointPolicyCommand.Inventory
                ? new EndpointAliasSession()
                : EndpointAliasSession.FromToken(options.SessionToken!);
            var inventoryService = new WindowsDefaultEndpointInventory(aliases);
            DefaultEndpointInventorySnapshot inventory = inventoryService.Collect();
            if (options.Command == DefaultEndpointPolicyCommand.Inventory)
            {
                WriteInventory(inventory, output);
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (inventory.Faults.Any(fault => fault.AffectsOwnership))
            {
                error.WriteLine(
                    "Endpoint ownership evidence is incomplete; refusing to select a default output.");
                return 1;
            }

            DefaultEndpointInventoryRow[] selectedRows = [.. inventory.Rows.Where(row =>
                string.Equals(row.ContainerAlias, options.ContainerAlias, StringComparison.Ordinal))];
            OpaqueContainerHandle[] selectedContainers = [.. selectedRows
                .Select(row => row.Candidate.Container)
                .Distinct()];
            if (selectedContainers.Length != 1)
            {
                error.WriteLine(
                    $"Container {options.ContainerAlias} did not resolve to exactly one current device.");
                return 1;
            }

            DefaultEndpointTargetResolution resolution = DefaultEndpointTargetResolver.Resolve(
                inventory.Rows.Select(row => row.Candidate),
                selectedContainers[0]);
            if (resolution.Status != DefaultEndpointTargetStatus.Selected || resolution.Target is null)
            {
                error.WriteLine($"Target resolution refused: {resolution.Status}.");
                return 1;
            }

            using var policy = new WindowsDefaultAudioEndpointPolicy();
            const long generation = 1;
            var coordinator = new DefaultEndpointSwitchCoordinator(
                policy,
                new FixedGenerationFence(generation));
            DefaultEndpointSwitchResult result = await coordinator.SwitchAsync(
                resolution.Target,
                generation,
                cancellationToken).ConfigureAwait(false);
            WriteResult(result, output);
            return result.Succeeded ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled; no automatic retry was attempted.");
            return 130;
        }
        catch (WindowsAudioInteropException exception)
        {
            error.WriteLine(
                $"Windows audio operation {exception.Operation} failed with HRESULT {FormatHResult(exception.NativeHResult)}.");
            return 1;
        }
        catch
        {
            error.WriteLine("Default-endpoint diagnostic failed before a sanitized result was produced.");
            return 1;
        }
    }

    private static void WriteInventory(
        DefaultEndpointInventorySnapshot inventory,
        TextWriter output)
    {
        output.WriteLine($"session={inventory.SessionToken}");
        output.WriteLine(
            $"Sanitized render inventory: {inventory.Rows.Count.ToString(CultureInfo.InvariantCulture)} endpoint(s)");
        foreach (DefaultEndpointInventoryRow row in inventory.Rows
            .OrderBy(row => row.ContainerAlias, StringComparer.Ordinal)
            .ThenBy(row => row.EndpointAlias, StringComparer.Ordinal))
        {
            string roles = row.DefaultRoles.Count == 0
                ? "none"
                : string.Join(',', row.DefaultRoles.OrderBy(role => role));
            output.WriteLine(
                $"container={row.ContainerAlias} endpoint={row.EndpointAlias} label={row.Label} state={row.Candidate.Availability} profile={row.Candidate.Profile} form-factor={row.FormFactor.ToString(CultureInfo.InvariantCulture)} defaults={roles}");
        }

        foreach (DefaultEndpointInventoryFault fault in inventory.Faults)
        {
            output.WriteLine(
                $"fault={fault.Operation} hresult={FormatHResult(fault.HResult)} ownership-impact={fault.AffectsOwnership.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        }
    }

    private static async Task<int> ProbeAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            using var policy = new WindowsDefaultAudioEndpointPolicy();
            foreach (DefaultEndpointRole role in Enum.GetValues<DefaultEndpointRole>())
            {
                OpaqueEndpointHandle? endpoint = await policy.GetDefaultEndpointAsync(
                    role,
                    cancellationToken).ConfigureAwait(false);
                output.WriteLine(
                    $"policy=available role={role} default-present={(endpoint is not null).ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled; no automatic retry was attempted.");
            return 130;
        }
        catch (WindowsAudioInteropException exception)
        {
            error.WriteLine(
                $"Windows audio operation {exception.Operation} failed with HRESULT {FormatHResult(exception.NativeHResult)}.");
            return 1;
        }
        catch
        {
            error.WriteLine("PolicyConfig compatibility probe failed without changing Windows.");
            return 1;
        }
    }

    private static void WriteResult(DefaultEndpointSwitchResult result, TextWriter output)
    {
        output.WriteLine(
            $"outcome={result.Outcome} generation={result.Generation.ToString(CultureInfo.InvariantCulture)} communications-unchanged={result.CommunicationsUnchanged.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} accepted-mutation={result.HasAcceptedMutation.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        foreach (DefaultEndpointRoleEvidence role in result.Roles)
        {
            string hResult = role.HResult is int value ? FormatHResult(value) : "none";
            output.WriteLine(
                $"role={role.Role} required={role.ChangeRequired.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} accepted={role.WriteAccepted.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} notified={role.NotificationObserved.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} readback={role.ReadBackMatched.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} hresult={hResult}");
        }
    }

    private static string FormatHResult(int hResult) =>
        $"0x{unchecked((uint)hResult):X8}";

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("QuickPods default-endpoint policy feasibility spike");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  inventory");
        output.WriteLine("      List sanitized render endpoints, form-factor classification, and current default roles without changing Windows.");
        output.WriteLine("  probe");
        output.WriteLine("      Activate the isolated PolicyConfig boundary and read default-role presence without changing Windows.");
        output.WriteLine("  apply --session TOKEN --container ALIAS --confirm-container ALIAS --confirm-default-endpoint-operation");
        output.WriteLine("      Set only Console and Multimedia to the one active stereo render endpoint in the confirmed Container.");
        output.WriteLine();
        output.WriteLine("Communications is never written. A write is not successful until notification and read-back both match.");
    }

    private sealed class FixedGenerationFence(long generation) : IDefaultEndpointGenerationFence
    {
        public bool IsCurrent(long candidateGeneration) => candidateGeneration == generation;
    }
}
