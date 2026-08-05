using System.Globalization;

namespace QuickPods.Spike.CoreAudio;

internal enum CoreAudioCommand
{
    Help,
    Status,
    Watch,
    Pulse,
    Exercise,
    KeyLatency,
}

internal sealed record CoreAudioOptions(
    CoreAudioCommand Command,
    int Seconds = 30,
    double? Percent = null,
    int HoldMilliseconds = 1000,
    int Iterations = 1000,
    double DeltaPercent = 1d,
    bool PlaybackStoppedConfirmed = false,
    string? CsvPath = null)
{
    public static CoreAudioOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            return new CoreAudioOptions(CoreAudioCommand.Help);
        }

        CoreAudioCommand command = arguments[0].ToLowerInvariant() switch
        {
            "help" or "--help" or "-h" => CoreAudioCommand.Help,
            "status" => CoreAudioCommand.Status,
            "watch" => CoreAudioCommand.Watch,
            "pulse" => CoreAudioCommand.Pulse,
            "exercise" => CoreAudioCommand.Exercise,
            "key-latency" => CoreAudioCommand.KeyLatency,
            _ => throw new ArgumentException($"Unknown command '{arguments[0]}'."),
        };

        int seconds = 30;
        double? percent = null;
        int holdMilliseconds = 1000;
        int iterations = command == CoreAudioCommand.KeyLatency ? 100 : 1000;
        double deltaPercent = 1d;
        bool playbackStoppedConfirmed = false;
        string? csvPath = null;

        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            switch (option)
            {
                case "--seconds":
                    seconds = ParseInt32(ReadValue(arguments, ref index, option), option, 1, 3600);
                    break;
                case "--percent":
                    percent = ParseDouble(ReadValue(arguments, ref index, option), option, 0d, 100d);
                    break;
                case "--hold-ms":
                    holdMilliseconds = ParseInt32(ReadValue(arguments, ref index, option), option, 0, 60_000);
                    break;
                case "--iterations":
                    iterations = ParseInt32(ReadValue(arguments, ref index, option), option, 1, 10_000);
                    break;
                case "--delta-percent":
                    deltaPercent = ParseDouble(ReadValue(arguments, ref index, option), option, 0.1d, 5d);
                    break;
                case "--csv":
                    csvPath = ReadValue(arguments, ref index, option);
                    break;
                case "--confirm-playback-stopped":
                    playbackStoppedConfirmed = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (command == CoreAudioCommand.Pulse && percent is null)
        {
            throw new ArgumentException("The pulse command requires --percent <0..100>.");
        }

        if (command == CoreAudioCommand.KeyLatency && iterations > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                iterations,
                "key-latency is limited to at most 1000 bounded key steps.");
        }

        if (command is not (CoreAudioCommand.Pulse or CoreAudioCommand.Exercise or CoreAudioCommand.KeyLatency) &&
            playbackStoppedConfirmed)
        {
            throw new ArgumentException(
                "--confirm-playback-stopped is only valid for pulse, exercise, or key-latency.");
        }

        return new CoreAudioOptions(
            command,
            seconds,
            percent,
            holdMilliseconds,
            iterations,
            deltaPercent,
            playbackStoppedConfirmed,
            csvPath);
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (++index >= arguments.Count)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return arguments[index];
    }

    private static int ParseInt32(string value, string option, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(
                option,
                value,
                $"{option} must be an integer from {minimum} through {maximum}.");
        }

        return parsed;
    }

    private static double ParseDouble(string value, string option, double minimum, double maximum)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(
                option,
                value,
                $"{option} must be a number from {minimum} through {maximum}.");
        }

        return parsed;
    }
}
