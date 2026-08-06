using System.Globalization;

namespace QuickPods.TaskbarObserver;

internal readonly record struct ObserverOptions(
    bool IsValid,
    string? PipeName,
    int ParentProcessId)
{
    internal static ObserverOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args is ["run", "--pipe-name", string pipeName, "--parent-pid", string parentText] &&
            IsValidPipeName(pipeName) &&
            int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out int parentProcessId) &&
            parentProcessId > 0)
        {
            return new(true, pipeName, parentProcessId);
        }

        return new(false, null, 0);
    }

    private static bool IsValidPipeName(string pipeName) =>
        pipeName is { Length: >= 16 and <= 128 } &&
        pipeName.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
}
