namespace QuickPods.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Phase 1 establishes composition boundaries only. Tray and WPF startup arrive in later phases.
        return 0;
    }
}
