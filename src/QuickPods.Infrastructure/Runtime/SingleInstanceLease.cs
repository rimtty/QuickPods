namespace QuickPods.Infrastructure.Runtime;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Semaphore instanceSemaphore;
    private readonly EventWaitHandle activationEvent;
    private RegisteredWaitHandle? activationRegistration;
    private bool ownsInstance;
    private bool disposed;

    private SingleInstanceLease(
        Semaphore instanceSemaphore,
        EventWaitHandle activationEvent,
        bool ownsInstance)
    {
        this.instanceSemaphore = instanceSemaphore;
        this.activationEvent = activationEvent;
        this.ownsInstance = ownsInstance;
    }

    public event EventHandler? ActivationRequested;

    public static SingleInstanceLease TryAcquire(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        string objectPrefix = $"Local\\{applicationId}";
        var instanceSemaphore = new Semaphore(
            initialCount: 0,
            maximumCount: 1,
            objectPrefix,
            out bool createdNew);
        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"{objectPrefix}.Activate");
        return new SingleInstanceLease(instanceSemaphore, activationEvent, createdNew);
    }

    public bool IsPrimary => ownsInstance;

    public void StartActivationListener()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ownsInstance)
        {
            throw new InvalidOperationException(
                "Only the primary QuickPods instance can listen for activation requests.");
        }

        activationRegistration ??= ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut && state is SingleInstanceLease lease)
                {
                    lease.ActivationRequested?.Invoke(lease, EventArgs.Empty);
                }
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ownsInstance)
        {
            throw new InvalidOperationException(
                "The primary QuickPods instance cannot signal itself.");
        }

        activationEvent.Set();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        activationRegistration?.Unregister(null);
        activationRegistration = null;
        if (ownsInstance)
        {
            ownsInstance = false;
        }

        activationEvent.Dispose();
        instanceSemaphore.Dispose();
    }
}
