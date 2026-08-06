namespace QuickPods.Infrastructure.Runtime;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex mutex;
    private bool ownsMutex;

    private SingleInstanceLease(Mutex mutex, bool ownsMutex)
    {
        this.mutex = mutex;
        this.ownsMutex = ownsMutex;
    }

    public static SingleInstanceLease TryAcquire(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var mutex = new Mutex(initiallyOwned: true, $"Local\\{applicationId}", out bool createdNew);
        return new SingleInstanceLease(mutex, createdNew);
    }

    public bool IsPrimary => ownsMutex;

    public void Dispose()
    {
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }

        mutex.Dispose();
    }
}
