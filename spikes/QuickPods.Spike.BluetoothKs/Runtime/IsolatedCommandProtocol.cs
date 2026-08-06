using System.Text.Json;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static class IsolatedCommandProtocol
{
    internal const string AuthorizationEnvironmentVariable =
        "QUICKPODS_BLUETOOTH_COMMAND_AUTH";
}

internal sealed record IsolatedCommandRequest(
    string RequestNonce,
    IReadOnlyList<string> Arguments)
{
    internal string Serialize(string authorizationToken)
    {
        var frame = new RequestFrame(
            KsChildProtocol.Version,
            authorizationToken,
            RequestNonce,
            Arguments);
        return JsonSerializer.Serialize(frame, KsChildProtocol.SerializerOptions);
    }

    internal static IsolatedCommandRequest DeserializeAndValidate(
        string json,
        string? expectedAuthorizationToken,
        bool sameExecutableParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RequestFrame frame = JsonSerializer.Deserialize<RequestFrame>(
            json,
            KsChildProtocol.SerializerOptions) ??
            throw new FormatException("The isolated command request was empty.");
        if (!sameExecutableParent ||
            frame.ProtocolVersion != KsChildProtocol.Version ||
            !KsChildProtocol.IsAuthorized(expectedAuthorizationToken, frame.AuthorizationToken) ||
            !KsChildProtocol.IsHexToken(frame.RequestNonce) ||
            frame.Arguments is null ||
            frame.Arguments.Count == 0 ||
            frame.Arguments.Count > 16 ||
            frame.Arguments.Any(argument => argument is null || argument.Length > 256))
        {
            throw new FormatException("The isolated command capability failed validation.");
        }

        return new IsolatedCommandRequest(frame.RequestNonce!, frame.Arguments);
    }

    private sealed record RequestFrame(
        int? ProtocolVersion,
        string? AuthorizationToken,
        string? RequestNonce,
        IReadOnlyList<string>? Arguments);
}

internal sealed record IsolatedCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    internal string Serialize(string requestNonce)
    {
        var frame = new ResponseFrame(
            KsChildProtocol.Version,
            requestNonce,
            ExitCode,
            StandardOutput,
            StandardError);
        return JsonSerializer.Serialize(frame, KsChildProtocol.SerializerOptions);
    }

    internal static IsolatedCommandResponse Deserialize(
        string json,
        string expectedRequestNonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ResponseFrame frame = JsonSerializer.Deserialize<ResponseFrame>(
            json,
            KsChildProtocol.SerializerOptions) ??
            throw new FormatException("The isolated command response was empty.");
        if (frame.ProtocolVersion != KsChildProtocol.Version ||
            !string.Equals(frame.RequestNonce, expectedRequestNonce, StringComparison.Ordinal) ||
            frame.ExitCode is null ||
            frame.StandardOutput is null ||
            frame.StandardError is null ||
            frame.StandardOutput.Length > 1_000_000 ||
            frame.StandardError.Length > 100_000)
        {
            throw new FormatException("The isolated command response failed validation.");
        }

        return new IsolatedCommandResponse(
            frame.ExitCode.Value,
            frame.StandardOutput,
            frame.StandardError);
    }

    private sealed record ResponseFrame(
        int? ProtocolVersion,
        string? RequestNonce,
        int? ExitCode,
        string? StandardOutput,
        string? StandardError);
}
