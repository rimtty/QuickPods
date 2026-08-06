namespace QuickPods.Presentation;

public sealed class ProductLifetimePolicy
{
    private int exitRequested;

    public bool IsExitRequested => Volatile.Read(ref exitRequested) != 0;

    public bool ShouldHideMainWindowOnClose => !IsExitRequested;

    public void RequestExit() => Interlocked.Exchange(ref exitRequested, 1);
}
