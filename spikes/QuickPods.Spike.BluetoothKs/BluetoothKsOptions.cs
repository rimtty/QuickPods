namespace QuickPods.Spike.BluetoothKs;

internal enum BluetoothKsCommand
{
    Help,
    Inventory,
    Probe,
    Connect,
    Disconnect,
}

internal sealed record BluetoothKsOptions(
    BluetoothKsCommand Command,
    string? SessionToken = null,
    string? TargetHash = null,
    string? ConfirmedTargetHash = null,
    bool KsOperationConfirmed = false,
    bool PlaybackStoppedConfirmed = false)
{
    private const int SanitizedHashLength = 24;
    private const int SessionTokenLength = 64;

    public static BluetoothKsOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            return new BluetoothKsOptions(BluetoothKsCommand.Help);
        }

        BluetoothKsCommand command = arguments[0].ToLowerInvariant() switch
        {
            "help" or "--help" or "-h" => BluetoothKsCommand.Help,
            "inventory" => BluetoothKsCommand.Inventory,
            "probe" => BluetoothKsCommand.Probe,
            "connect" => BluetoothKsCommand.Connect,
            "disconnect" => BluetoothKsCommand.Disconnect,
            _ => throw new ArgumentException($"Unknown command '{arguments[0]}'."),
        };

        string? sessionToken = null;
        string? targetHash = null;
        string? confirmedTargetHash = null;
        bool ksOperationConfirmed = false;
        bool playbackStoppedConfirmed = false;

        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            switch (option)
            {
                case "--session":
                    sessionToken = NormalizeHex(
                        ReadValue(arguments, ref index, option),
                        option,
                        SessionTokenLength);
                    break;
                case "--target":
                    targetHash = NormalizeHex(
                        ReadValue(arguments, ref index, option),
                        option,
                        SanitizedHashLength);
                    break;
                case "--confirm-target":
                    confirmedTargetHash = NormalizeHex(
                        ReadValue(arguments, ref index, option),
                        option,
                        SanitizedHashLength);
                    break;
                case "--confirm-ks-operation":
                    ksOperationConfirmed = true;
                    break;
                case "--confirm-playback-stopped":
                    playbackStoppedConfirmed = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        Validate(
            command,
            sessionToken,
            targetHash,
            confirmedTargetHash,
            ksOperationConfirmed,
            playbackStoppedConfirmed);

        return new BluetoothKsOptions(
            command,
            sessionToken,
            targetHash,
            confirmedTargetHash,
            ksOperationConfirmed,
            playbackStoppedConfirmed);
    }

    private static void Validate(
        BluetoothKsCommand command,
        string? sessionToken,
        string? targetHash,
        string? confirmedTargetHash,
        bool ksOperationConfirmed,
        bool playbackStoppedConfirmed)
    {
        if (command is BluetoothKsCommand.Help or BluetoothKsCommand.Inventory)
        {
            if (sessionToken is not null ||
                targetHash is not null ||
                confirmedTargetHash is not null ||
                ksOperationConfirmed ||
                playbackStoppedConfirmed)
            {
                throw new ArgumentException($"The {command.ToString().ToLowerInvariant()} command does not accept operation options.");
            }

            return;
        }

        if (sessionToken is null)
        {
            throw new ArgumentException(
                "The command requires --session <64-character report session token>.");
        }

        if (targetHash is null)
        {
            throw new ArgumentException("The command requires --target <24-character report-scoped alias>.");
        }

        if (!ksOperationConfirmed)
        {
            throw new ArgumentException("The command requires --confirm-ks-operation.");
        }

        if (command == BluetoothKsCommand.Probe)
        {
            if (confirmedTargetHash is not null || playbackStoppedConfirmed)
            {
                throw new ArgumentException(
                    "The probe command accepts only --session, --target, and --confirm-ks-operation.");
            }

            return;
        }

        if (!string.Equals(targetHash, confirmedTargetHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("--confirm-target must exactly repeat the sanitized --target hash.");
        }

        if (!playbackStoppedConfirmed)
        {
            throw new ArgumentException("Stop audio playback, then pass --confirm-playback-stopped.");
        }
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (++index >= arguments.Count)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return arguments[index];
    }

    private static string NormalizeHex(
        string value,
        string option,
        int requiredLength)
    {
        if (value.Length != requiredLength || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                $"{option} must be a {requiredLength}-character hexadecimal value.");
        }

        return value.ToUpperInvariant();
    }
}
