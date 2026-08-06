using QuickPods.Contracts;

namespace QuickPods.TaskbarHost;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Protocol ownership is fixed in Phase 1; native hosting and IPC arrive in Phase 3.
        return QuickPodsProtocol.Version == 1 ? 0 : 1;
    }
}
