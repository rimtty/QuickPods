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

    private static bool TryOpen(string uri)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
