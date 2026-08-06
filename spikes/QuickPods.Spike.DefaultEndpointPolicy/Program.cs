using QuickPods.Spike.DefaultEndpointPolicy;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArguments) =>
{
    eventArguments.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    return await DefaultEndpointPolicyCli.RunAsync(
        args,
        Console.Out,
        Console.Error,
        cancellation.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
