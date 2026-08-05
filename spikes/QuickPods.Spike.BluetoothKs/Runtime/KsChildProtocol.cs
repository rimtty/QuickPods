using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static class KsChildProtocol
{
    internal const int Version = 1;
    internal const string AuthorizationEnvironmentVariable =
        "QUICKPODS_BLUETOOTH_KS_CHILD_AUTH";
    internal const string SimulationTarget = "SIMULATION";

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static string CreateAuthorizationToken() =>
        RandomNumberGenerator.GetHexString(32);

    internal static string CreateRequestNonce() =>
        RandomNumberGenerator.GetHexString(32);

    internal static bool IsAuthorized(string? expectedToken, string? suppliedToken)
    {
        if (!IsHexToken(expectedToken) ||
            !IsHexToken(suppliedToken) ||
            expectedToken!.Length != suppliedToken!.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedToken),
            Encoding.ASCII.GetBytes(suppliedToken));
    }

    internal static bool IsSanitizedHash(string value) =>
        value.Length == 24 && value.All(Uri.IsHexDigit);

    internal static bool IsHexToken(string? value) =>
        value is { Length: 32 } && value.All(Uri.IsHexDigit);
}

internal enum KsChildOperation
{
    BasicSupportReconnect,
    BasicSupportDisconnect,
    Reconnect,
    Disconnect,
    SimulatedHang,
    SimulatedFault,
}

internal readonly record struct KsChildConsent(
    bool KsOperationConfirmed,
    bool PlaybackStoppedConfirmed,
    bool SimulationConfirmed)
{
    internal static KsChildConsent Probe { get; } = new(
        KsOperationConfirmed: true,
        PlaybackStoppedConfirmed: false,
        SimulationConfirmed: false);

    internal static KsChildConsent Mutation { get; } = new(
        KsOperationConfirmed: true,
        PlaybackStoppedConfirmed: true,
        SimulationConfirmed: false);

    internal static KsChildConsent Simulation { get; } = new(
        KsOperationConfirmed: false,
        PlaybackStoppedConfirmed: false,
        SimulationConfirmed: true);
}

internal sealed record KsChildInvocation(
    string TargetHash,
    string AdapterDeviceId,
    KsChildOperation Operation,
    KsChildConsent Consent);

internal sealed record KsChildRequest(
    string RequestNonce,
    string TargetHash,
    string AdapterDeviceId,
    KsChildOperation Operation,
    KsChildConsent Consent)
{
    internal string Serialize(string authorizationToken)
    {
        var frame = new RequestFrame(
            KsChildProtocol.Version,
            authorizationToken,
            RequestNonce,
            TargetHash,
            AdapterDeviceId,
            Operation.ToString(),
            Consent.KsOperationConfirmed,
            Consent.PlaybackStoppedConfirmed,
            Consent.SimulationConfirmed);
        return JsonSerializer.Serialize(frame, KsChildProtocol.SerializerOptions);
    }

    internal static KsChildRequest DeserializeAndValidate(
        string json,
        string? expectedAuthorizationToken,
        KsChildOperation expectedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RequestFrame frame = JsonSerializer.Deserialize<RequestFrame>(
            json,
            KsChildProtocol.SerializerOptions) ??
            throw new FormatException("The KS child request was empty.");
        if (frame.ProtocolVersion != KsChildProtocol.Version ||
            !KsChildProtocol.IsAuthorized(expectedAuthorizationToken, frame.AuthorizationToken) ||
            !KsChildProtocol.IsHexToken(frame.RequestNonce) ||
            !Enum.TryParse(frame.Operation, ignoreCase: false, out KsChildOperation operation) ||
            !Enum.IsDefined(operation) ||
            operation != expectedOperation ||
            string.IsNullOrWhiteSpace(frame.AdapterDeviceId) ||
            frame.AdapterDeviceId.Length > 8192)
        {
            throw new FormatException("The KS child request failed validation.");
        }

        bool simulation = operation is KsChildOperation.SimulatedHang or KsChildOperation.SimulatedFault;
        bool targetValid = simulation
            ? string.Equals(
                frame.TargetHash,
                KsChildProtocol.SimulationTarget,
                StringComparison.Ordinal)
            : frame.TargetHash is not null && KsChildProtocol.IsSanitizedHash(frame.TargetHash);
        bool consentValid = operation switch
        {
            KsChildOperation.BasicSupportReconnect or
            KsChildOperation.BasicSupportDisconnect =>
                frame.KsOperationConfirmed == true &&
                frame.PlaybackStoppedConfirmed == false &&
                frame.SimulationConfirmed == false,
            KsChildOperation.Reconnect or KsChildOperation.Disconnect =>
                frame.KsOperationConfirmed == true &&
                frame.PlaybackStoppedConfirmed == true &&
                frame.SimulationConfirmed == false,
            KsChildOperation.SimulatedHang or KsChildOperation.SimulatedFault =>
                frame.KsOperationConfirmed == false &&
                frame.PlaybackStoppedConfirmed == false &&
                frame.SimulationConfirmed == true,
            _ => false,
        };
        if (!targetValid || !consentValid)
        {
            throw new FormatException("The KS child capability was not valid for the requested operation.");
        }

        return new KsChildRequest(
            frame.RequestNonce!,
            frame.TargetHash!,
            frame.AdapterDeviceId,
            operation,
            new KsChildConsent(
                frame.KsOperationConfirmed!.Value,
                frame.PlaybackStoppedConfirmed!.Value,
                frame.SimulationConfirmed!.Value));
    }

    private sealed record RequestFrame(
        int? ProtocolVersion,
        string? AuthorizationToken,
        string? RequestNonce,
        string? TargetHash,
        string? AdapterDeviceId,
        string? Operation,
        bool? KsOperationConfirmed,
        bool? PlaybackStoppedConfirmed,
        bool? SimulationConfirmed);
}

internal sealed record KsChildResponse(
    int HResult,
    uint BytesReturned,
    uint? SupportFlags)
{
    public string Serialize(
        string requestNonce,
        string targetHash,
        KsChildOperation operation)
    {
        var frame = new ResponseFrame(
            KsChildProtocol.Version,
            requestNonce,
            targetHash,
            operation.ToString(),
            HResult,
            BytesReturned,
            SupportFlags);
        return JsonSerializer.Serialize(frame, KsChildProtocol.SerializerOptions);
    }

    public static KsChildResponse Deserialize(
        string json,
        string expectedRequestNonce,
        string expectedTargetHash,
        KsChildOperation expectedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ResponseFrame frame = JsonSerializer.Deserialize<ResponseFrame>(
            json,
            KsChildProtocol.SerializerOptions) ??
            throw new FormatException("The KS child response was empty.");
        if (frame.ProtocolVersion != KsChildProtocol.Version ||
            !string.Equals(frame.RequestNonce, expectedRequestNonce, StringComparison.Ordinal) ||
            !string.Equals(frame.TargetHash, expectedTargetHash, StringComparison.Ordinal) ||
            !string.Equals(frame.Operation, expectedOperation.ToString(), StringComparison.Ordinal) ||
            frame.HResult is null ||
            frame.BytesReturned is null)
        {
            throw new FormatException("The KS child response failed validation.");
        }

        return new KsChildResponse(
            frame.HResult.Value,
            frame.BytesReturned.Value,
            frame.SupportFlags);
    }

    private sealed record ResponseFrame(
        int? ProtocolVersion,
        string? RequestNonce,
        string? TargetHash,
        string? Operation,
        int? HResult,
        uint? BytesReturned,
        uint? SupportFlags);
}
