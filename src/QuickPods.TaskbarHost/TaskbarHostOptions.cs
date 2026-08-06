using System.Globalization;

namespace QuickPods.TaskbarHost;

internal enum TaskbarHostCommand
{
    Help,
    Inspect,
    Preview,
    Invalid,
}

internal readonly record struct TaskbarHostOptions(
    TaskbarHostCommand Command,
    TimeSpan PreviewDuration)
{
    private const int MaximumPreviewSeconds = 60;

    internal static TaskbarHostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            return new(TaskbarHostCommand.Help, TimeSpan.Zero);
        }

        if (args is ["inspect"])
        {
            return new(TaskbarHostCommand.Inspect, TimeSpan.Zero);
        }

        if (args is ["preview", "--confirm-live-host", "--duration-seconds", string secondsText] &&
            int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) &&
            seconds is >= 1 and <= MaximumPreviewSeconds)
        {
            return new(TaskbarHostCommand.Preview, TimeSpan.FromSeconds(seconds));
        }

        return new(TaskbarHostCommand.Invalid, TimeSpan.Zero);
    }
}
