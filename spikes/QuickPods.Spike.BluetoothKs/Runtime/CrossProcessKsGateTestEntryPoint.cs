using System.Globalization;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static class CrossProcessKsGateTestEntryPoint
{
    internal const string Switch = "--cross-process-gate-test-child";

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out TimeSpan holdDuration)
    {
        holdDuration = TimeSpan.Zero;
        if (arguments.Count != 2 ||
            !string.Equals(arguments[0], Switch, StringComparison.Ordinal) ||
            !int.TryParse(
                arguments[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int milliseconds) ||
            milliseconds is < 1 or > 5_000)
        {
            return false;
        }

        holdDuration = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    internal static async Task<int> RunAsync(
        TimeSpan holdDuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        CrossProcessKsGateResult<int> result = await CrossProcessKsGate.RunAsync(
            () =>
            {
                output.WriteLine("acquired");
                output.Flush();
                if (cancellationToken.WaitHandle.WaitOne(holdDuration))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return 0;
            },
            cancellationToken).ConfigureAwait(false);
        if (result.Status != CrossProcessKsGateStatus.Executed)
        {
            output.WriteLine(result.Status == CrossProcessKsGateStatus.Busy
                ? "busy"
                : "abandoned");
            return 3;
        }

        return result.Value;
    }
}
