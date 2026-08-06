namespace QuickPods.TaskbarHost.Discovery;

internal static class AutomationMta
{
    public static async Task<T> RunAsync<T>(
        Func<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(operation());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "QuickPods one-shot UI Automation scan",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
}
