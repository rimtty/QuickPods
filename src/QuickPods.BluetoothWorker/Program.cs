using QuickPods.Windows.Bluetooth.Worker;

if (args.Length != 2 ||
    !string.Equals(args[0], "--operation", StringComparison.Ordinal) ||
    !Enum.TryParse(args[1], ignoreCase: false, out BluetoothWorkerOperation operation) ||
    !Enum.IsDefined(operation))
{
    Console.Error.WriteLine("Bluetooth worker invocation was invalid.");
    return 2;
}

return BluetoothWorkerEntryPoint.Run(
    operation,
    Environment.GetEnvironmentVariable(BluetoothWorkerProtocol.AuthorizationEnvironmentVariable),
    BluetoothWorkerParentVerifier.IsExpectedAppParent(),
    Console.In,
    Console.Out,
    Console.Error);
