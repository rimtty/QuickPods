namespace QuickPods.Spike.DefaultEndpointPolicy;

internal enum DefaultEndpointPolicyCommand
{
    Help,
    Inventory,
    Probe,
    Apply,
}

internal sealed record DefaultEndpointPolicyOptions(
    DefaultEndpointPolicyCommand Command,
    string? SessionToken = null,
    string? ContainerAlias = null,
    string? ConfirmedContainerAlias = null,
    bool MutationConfirmed = false)
{
    private const int AliasLength = 24;
    private const int SessionLength = 64;

    internal static DefaultEndpointPolicyOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return new DefaultEndpointPolicyOptions(DefaultEndpointPolicyCommand.Help);
        }

        DefaultEndpointPolicyCommand command = arguments[0].ToLowerInvariant() switch
        {
            "help" or "--help" or "-h" => DefaultEndpointPolicyCommand.Help,
            "inventory" => DefaultEndpointPolicyCommand.Inventory,
            "probe" => DefaultEndpointPolicyCommand.Probe,
            "apply" => DefaultEndpointPolicyCommand.Apply,
            _ => throw new ArgumentException($"Unknown command '{arguments[0]}'."),
        };
        string? session = null;
        string? container = null;
        string? confirmedContainer = null;
        bool mutationConfirmed = false;
        for (int index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--session":
                    session = NormalizeHex(ReadValue(arguments, ref index, "--session"), SessionLength);
                    break;
                case "--container":
                    container = NormalizeHex(ReadValue(arguments, ref index, "--container"), AliasLength);
                    break;
                case "--confirm-container":
                    confirmedContainer = NormalizeHex(
                        ReadValue(arguments, ref index, "--confirm-container"),
                        AliasLength);
                    break;
                case "--confirm-default-endpoint-operation":
                    mutationConfirmed = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arguments[index]}'.");
            }
        }

        Validate(command, session, container, confirmedContainer, mutationConfirmed);
        return new DefaultEndpointPolicyOptions(
            command,
            session,
            container,
            confirmedContainer,
            mutationConfirmed);
    }

    private static void Validate(
        DefaultEndpointPolicyCommand command,
        string? session,
        string? container,
        string? confirmedContainer,
        bool mutationConfirmed)
    {
        if (command is DefaultEndpointPolicyCommand.Help or
            DefaultEndpointPolicyCommand.Inventory or
            DefaultEndpointPolicyCommand.Probe)
        {
            if (session is not null || container is not null || confirmedContainer is not null || mutationConfirmed)
            {
                throw new ArgumentException(
                    $"The {command.ToString().ToLowerInvariant()} command does not accept operation options.");
            }

            return;
        }

        if (session is null)
        {
            throw new ArgumentException("The apply command requires --session.");
        }

        if (container is null)
        {
            throw new ArgumentException("The apply command requires --container.");
        }

        if (!string.Equals(container, confirmedContainer, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "--confirm-container must exactly repeat the sanitized --container alias.");
        }

        if (!mutationConfirmed)
        {
            throw new ArgumentException(
                "The apply command requires --confirm-default-endpoint-operation.");
        }
    }

    private static string ReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string option)
    {
        if (++index >= arguments.Count)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return arguments[index];
    }

    private static string NormalizeHex(string value, int expectedLength)
    {
        if (value.Length != expectedLength || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                $"The value must contain {expectedLength} hexadecimal characters.");
        }

        return value.ToUpperInvariant();
    }
}
