using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickPods.Launcher;

internal static partial class Program
{
    private const uint MessageBoxErrorIcon = 0x00000010;
    private const uint MessageBoxOk = 0x00000000;

    [STAThread]
    private static int Main(string[] args) => LauncherRuntime.Run(
        args,
        AppContext.BaseDirectory,
        StartApplication,
        ShowError);

    private static int StartApplication(ProcessStartInfo startInfo, bool waitForExit)
    {
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Windows did not start the QuickPods application process.");
        if (!waitForExit)
        {
            return 0;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static void ShowError(string message) => _ = MessageBox(
        0,
        message,
        "QuickPods",
        MessageBoxOk | MessageBoxErrorIcon);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(
        nint windowHandle,
        string text,
        string caption,
        uint type);
}
