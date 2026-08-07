using System.Diagnostics;

namespace QuickPods.Windows.Settings;

public interface IWindowsSettingsLauncher
{
    bool TryOpenSoundSettings();

    bool TryOpenBluetoothSettings();
}

public sealed class WindowsSettingsLauncher : IWindowsSettingsLauncher
{
    private const string SoundSettingsUri = "ms-settings:sound";
    private const string BluetoothSettingsUri = "ms-settings:bluetooth";

    public bool TryOpenSoundSettings() => TryOpen(SoundSettingsUri);

    public bool TryOpenBluetoothSettings() => TryOpen(BluetoothSettingsUri);

    private static bool TryOpen(string uri) => TryOpen(uri, Process.Start);

    internal static bool TryOpen(
        string uri,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(startProcess);

        try
        {
            using Process? process = startProcess(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });

            // Shell URI activation can complete successfully without returning a
            // Process instance. The absence of an exception is the success signal.
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
