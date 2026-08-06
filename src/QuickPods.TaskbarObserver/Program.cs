namespace QuickPods.TaskbarObserver;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ObserverOptions options = ObserverOptions.Parse(args);
        return options.IsValid
            ? ObserverRuntime.Run(options.PipeName!, options.ParentProcessId)
            : 2;
    }
}
