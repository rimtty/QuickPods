using System.Diagnostics;

namespace QuickPods.Launcher;

internal static class LauncherRuntime
{
    internal const string ApplicationDirectoryName = "app";
    internal const string ApplicationExecutableName = "QuickPods.exe";
    private const string UnregisterStartupArgument = "--unregister-startup";

    internal static int Run(
        IReadOnlyList<string> args,
        string launcherDirectory,
        Func<ProcessStartInfo, bool, int> startApplication,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherDirectory);
        ArgumentNullException.ThrowIfNull(startApplication);
        ArgumentNullException.ThrowIfNull(showError);

        string applicationDirectory = Path.GetFullPath(Path.Combine(
            launcherDirectory,
            ApplicationDirectoryName));
        string applicationExecutable = Path.Combine(
            applicationDirectory,
            ApplicationExecutableName);
        if (!File.Exists(applicationExecutable))
        {
            showError(CreateMissingApplicationMessage(applicationExecutable));
            return 2;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = applicationExecutable,
            WorkingDirectory = applicationDirectory,
            UseShellExecute = false,
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        bool waitForExit = args.Any(argument => string.Equals(
            argument,
            UnregisterStartupArgument,
            StringComparison.OrdinalIgnoreCase));
        try
        {
            return startApplication(startInfo, waitForExit);
        }
        catch (Exception exception)
        {
            showError(CreateLaunchFailureMessage(exception.Message));
            return 3;
        }
    }

    private static string CreateMissingApplicationMessage(string applicationExecutable) =>
        $"QuickPods could not find its application files. Re-extract the complete ZIP package.\n\n" +
        $"QuickPodsのアプリケーションファイルが見つかりません。ZIP全体をもう一度展開してください。\n\n" +
        applicationExecutable;

    private static string CreateLaunchFailureMessage(string detail) =>
        $"QuickPods could not start.\n\nQuickPodsを起動できませんでした。\n\n{detail}";
}
