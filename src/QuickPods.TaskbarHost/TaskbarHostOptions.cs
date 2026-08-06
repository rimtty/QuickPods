using System.Globalization;

namespace QuickPods.TaskbarHost;

internal enum TaskbarHostCommand
{
    Help,
    Inspect,
    Preview,
    Run,
    Invalid,
}

internal readonly record struct TaskbarHostOptions(
    TaskbarHostCommand Command,
    TimeSpan PreviewDuration,
    string? PipeName,
    int ParentProcessId)
{
    private const int MaximumPreviewSeconds = 60;

    internal static TaskbarHostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            return new(TaskbarHostCommand.Help, TimeSpan.Zero, null, 0);
        }

        if (args is ["inspect"])
        {
            return new(TaskbarHostCommand.Inspect, TimeSpan.Zero, null, 0);
        }

        if (args is ["preview", "--confirm-live-host", "--duration-seconds", string secondsText] &&
            int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) &&
            seconds is >= 1 and <= MaximumPreviewSeconds)
        {
            return new(TaskbarHostCommand.Preview, TimeSpan.FromSeconds(seconds), null, 0);
        }

        if (args is ["run", "--pipe-name", string pipeName, "--parent-pid", string parentText] &&
            IsValidPipeName(pipeName) &&
            int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out int parentProcessId) &&
            parentProcessId > 0)
        {
            return new(TaskbarHostCommand.Run, TimeSpan.Zero, pipeName, parentProcessId);
        }

        return new(TaskbarHostCommand.Invalid, TimeSpan.Zero, null, 0);
    }

    private static bool IsValidPipeName(string pipeName) =>
        pipeName is { Length: >= 16 and <= 128 } &&
        pipeName.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
}
