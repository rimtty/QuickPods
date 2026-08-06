namespace QuickPods.Windows.Startup;

public static class StartupRegistrationCommand
{
    public static string Create(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The startup target must be the main QuickPods executable.",
                nameof(executablePath));
        }

        return $"\"{fullPath}\" --background";
    }
}
