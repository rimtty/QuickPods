using System.Globalization;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static class IsolatedCommandEntryPoint
{
    internal static async Task<int> RunAsync(
        string? expectedAuthorizationToken,
        bool sameExecutableParent,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        IsolatedCommandRequest request;
        BluetoothKsOptions options;
        try
        {
            request = IsolatedCommandRequest.DeserializeAndValidate(
                input.ReadLine() ?? string.Empty,
                expectedAuthorizationToken,
                sameExecutableParent);
            options = BluetoothKsOptions.Parse(request.Arguments);
            if (options.Command == BluetoothKsCommand.Help)
            {
                throw new FormatException("Help does not require an isolated command.");
            }
        }
        catch
        {
            error.WriteLine("Isolated command capability validation failed.");
            return 2;
        }

        using var commandOutput = new StringWriter(CultureInfo.InvariantCulture);
        using var commandError = new StringWriter(CultureInfo.InvariantCulture);
        int exitCode = await BluetoothKsCli.ExecuteLocalAsync(
            options,
            commandOutput,
            commandError,
            CancellationToken.None).ConfigureAwait(false);
        var response = new IsolatedCommandResponse(
            exitCode,
            commandOutput.ToString(),
            commandError.ToString());
        output.WriteLine(response.Serialize(request.RequestNonce));
        return 0;
    }
}
