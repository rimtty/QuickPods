using Microsoft.Win32;

namespace QuickPods.Windows.Startup;

public sealed class WindowsCurrentUserStartupRegistry : IStartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadCommand(string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string;
    }

    public void WriteCommand(string valueName, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(
            RunKeyPath,
            writable: true);
        runKey.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteCommand(string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
