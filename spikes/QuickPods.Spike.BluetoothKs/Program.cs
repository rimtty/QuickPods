using QuickPods.Spike.BluetoothKs;
using QuickPods.Spike.BluetoothKs.Runtime;

if (CrossProcessKsGateTestEntryPoint.TryParse(args, out TimeSpan holdDuration))
{
    return await CrossProcessKsGateTestEntryPoint.RunAsync(
        holdDuration,
        Console.Out,
        CancellationToken.None).ConfigureAwait(false);
}

if (args.Length == 1 &&
    string.Equals(args[0], "--isolated-command", StringComparison.Ordinal))
{
    string? expectedAuthorizationToken = Environment.GetEnvironmentVariable(
        IsolatedCommandProtocol.AuthorizationEnvironmentVariable);
    Environment.SetEnvironmentVariable(
        IsolatedCommandProtocol.AuthorizationEnvironmentVariable,
        value: null);
    return await IsolatedCommandEntryPoint.RunAsync(
        expectedAuthorizationToken,
        ParentProcessVerifier.IsSameExecutableParent(),
        Console.In,
        Console.Out,
        Console.Error).ConfigureAwait(false);
}

if (args.Length == 2 &&
    string.Equals(args[0], "--ks-child", StringComparison.Ordinal) &&
    Enum.TryParse(args[1], ignoreCase: false, out KsChildOperation childOperation) &&
    Enum.IsDefined(childOperation))
{
    string? expectedAuthorizationToken = Environment.GetEnvironmentVariable(
        KsChildProtocol.AuthorizationEnvironmentVariable);
    Environment.SetEnvironmentVariable(
        KsChildProtocol.AuthorizationEnvironmentVariable,
        value: null);
    return KsChildEntryPoint.Run(
        childOperation,
        expectedAuthorizationToken,
        ParentProcessVerifier.IsSameExecutableParent(),
        Console.In,
        Console.Out,
        Console.Error);
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArguments) =>
{
    eventArguments.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    return await BluetoothKsCli.RunAsync(
        args,
        Console.Out,
        Console.Error,
        cancellation.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
