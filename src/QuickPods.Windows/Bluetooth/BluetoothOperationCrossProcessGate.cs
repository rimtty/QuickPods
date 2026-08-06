using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Windows.Bluetooth;

public sealed class WindowsBluetoothOperationGate : IBluetoothOperationGatePort
{
    internal const string ProductMutexName = @"Global\QuickPods.Bluetooth.Operation.v1";
    private static readonly TimeSpan ProductAcquisitionTimeout = TimeSpan.FromMilliseconds(250);

    private readonly string mutexName;
    private readonly TimeSpan acquisitionTimeout;

    public WindowsBluetoothOperationGate()
        : this(ProductMutexName, ProductAcquisitionTimeout)
    {
    }

    internal WindowsBluetoothOperationGate(string mutexName, TimeSpan acquisitionTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(acquisitionTimeout, TimeSpan.Zero);
        this.mutexName = mutexName;
        this.acquisitionTimeout = acquisitionTimeout;
    }

    public ValueTask<BluetoothOperationAdmissionResult<T>> RunAsync<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Task<BluetoothOperationAdmissionResult<T>> task = Task.Factory.StartNew(
            () => Run(operation, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return new(task);
    }

    private BluetoothOperationAdmissionResult<T> Run<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool acquired = false;
        bool abandoned = false;
        try
        {
            int signaled;
            try
            {
                signaled = WaitHandle.WaitAny(
                    [mutex, cancellationToken.WaitHandle],
                    acquisitionTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                abandoned = true;
                signaled = 0;
            }

            if (signaled == WaitHandle.WaitTimeout)
            {
                return new(BluetoothOperationAdmissionStatus.Busy, default!);
            }

            if (signaled != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The Bluetooth operation gate wait was invalid.");
            }

            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (abandoned)
            {
                return new(BluetoothOperationAdmissionStatus.Abandoned, default!);
            }

            return new(
                BluetoothOperationAdmissionStatus.Executed,
                operation().AsTask().GetAwaiter().GetResult());
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
