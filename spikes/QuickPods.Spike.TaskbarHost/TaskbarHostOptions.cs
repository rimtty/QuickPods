using System.Globalization;

namespace QuickPods.Spike.TaskbarHost;

internal enum TaskbarCommand
{
    Help,
    Inspect,
    Host,
}

internal enum RequestedHostStyle
{
    Child,
    Popup,
}

internal enum RequestedFallbackMode
{
    Floating,
    Hidden,
}

internal sealed record TaskbarHostOptions(
    TaskbarCommand Command,
    RequestedHostStyle Style,
    RequestedFallbackMode Fallback,
    TimeSpan Duration,
    bool LiveHostConfirmed)
{
    internal const int MinimumDurationSeconds = 1;
    internal const int MaximumDurationSeconds = 120;

    public static OptionsParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || IsHelp(args[0]))
        {
            return OptionsParseResult.Success(
                new TaskbarHostOptions(
                    TaskbarCommand.Help,
                    RequestedHostStyle.Child,
                    RequestedFallbackMode.Floating,
                    TimeSpan.Zero,
                    false));
        }

        return args[0] switch
        {
            "inspect" => ParseInspect(args),
            "host" => ParseHost(args),
            _ => OptionsParseResult.Failure($"Unknown command: {args[0]}"),
        };
    }

    private static OptionsParseResult ParseInspect(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            return OptionsParseResult.Failure("The inspect command does not accept additional arguments.");
        }

        return OptionsParseResult.Success(
            new TaskbarHostOptions(
                TaskbarCommand.Inspect,
                RequestedHostStyle.Child,
                RequestedFallbackMode.Floating,
                TimeSpan.Zero,
                false));
    }

    private static OptionsParseResult ParseHost(IReadOnlyList<string> args)
    {
        RequestedHostStyle style = RequestedHostStyle.Popup;
        RequestedFallbackMode fallback = RequestedFallbackMode.Floating;
        int durationSeconds = 15;
        bool confirmed = false;
        bool styleSeen = false;
        bool fallbackSeen = false;
        bool durationSeen = false;

        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--style":
                    if (styleSeen || !TryTakeValue(args, ref index, out string? styleValue))
                    {
                        return OptionsParseResult.Failure("--style must appear once with child or popup.");
                    }

                    styleSeen = true;
                    if (!TryParseStyle(styleValue, out style))
                    {
                        return OptionsParseResult.Failure("--style must be child or popup.");
                    }

                    break;

                case "--fallback":
                    if (fallbackSeen || !TryTakeValue(args, ref index, out string? fallbackValue))
                    {
                        return OptionsParseResult.Failure(
                            "--fallback must appear once with floating or hidden.");
                    }

                    fallbackSeen = true;
                    if (!TryParseFallback(fallbackValue, out fallback))
                    {
                        return OptionsParseResult.Failure(
                            "--fallback must be floating or hidden.");
                    }

                    break;

                case "--duration":
                    if (durationSeen || !TryTakeValue(args, ref index, out string? durationValue))
                    {
                        return OptionsParseResult.Failure("--duration must appear once with a whole number of seconds.");
                    }

                    durationSeen = true;
                    if (!int.TryParse(
                            durationValue,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out durationSeconds) ||
                        durationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
                    {
                        return OptionsParseResult.Failure(
                            $"--duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds.");
                    }

                    break;

                case "--confirm-live-host":
                    if (confirmed)
                    {
                        return OptionsParseResult.Failure("--confirm-live-host must not be repeated.");
                    }

                    confirmed = true;
                    break;

                default:
                    return OptionsParseResult.Failure($"Unknown host argument: {argument}");
            }
        }

        if (!confirmed)
        {
            return OptionsParseResult.Failure(
                "Visible taskbar hosting requires --confirm-live-host. Read-only inspect does not require confirmation.");
        }

        return OptionsParseResult.Success(
            new TaskbarHostOptions(
                TaskbarCommand.Host,
                style,
                fallback,
                TimeSpan.FromSeconds(durationSeconds),
                true));
    }

    private static bool IsHelp(string value) => value is "help" or "--help" or "-h";

    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        int valueIndex = index + 1;
        if (valueIndex >= args.Count || args[valueIndex].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        index = valueIndex;
        value = args[valueIndex];
        return true;
    }

    private static bool TryParseStyle(string? value, out RequestedHostStyle style)
    {
        switch (value)
        {
            case "child":
                style = RequestedHostStyle.Child;
                return true;
            case "popup":
                style = RequestedHostStyle.Popup;
                return true;
            default:
                style = default;
                return false;
        }
    }

    private static bool TryParseFallback(string? value, out RequestedFallbackMode fallback)
    {
        switch (value)
        {
            case "floating":
                fallback = RequestedFallbackMode.Floating;
                return true;
            case "hidden":
                fallback = RequestedFallbackMode.Hidden;
                return true;
            default:
                fallback = default;
                return false;
        }
    }
}

internal sealed record OptionsParseResult(TaskbarHostOptions? Options, string? Error)
{
    public bool IsSuccess => Options is not null && Error is null;

    public static OptionsParseResult Success(TaskbarHostOptions options) => new(options, null);

    public static OptionsParseResult Failure(string error) => new(null, error);
}
