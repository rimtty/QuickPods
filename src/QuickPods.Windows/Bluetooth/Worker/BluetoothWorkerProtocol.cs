using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickPods.Core.Models;

namespace QuickPods.Windows.Bluetooth.Worker;

internal enum BluetoothWorkerOperation
{
    BasicSupportReconnect,
    BasicSupportDisconnect,
    Reconnect,
    Disconnect,
}

internal static class BluetoothWorkerProtocol
{
    internal const int Version = 1;
    internal const string AuthorizationEnvironmentVariable = "QUICKPODS_BLUETOOTH_WORKER_AUTH";

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static string CreateToken() => RandomNumberGenerator.GetHexString(32);

    internal static bool IsToken(string? value) =>
        value is { Length: 32 } && value.All(Uri.IsHexDigit);

    internal static bool IsAuthorized(string? expected, string? supplied)
    {
        if (!IsToken(expected) || !IsToken(supplied) || expected!.Length != supplied!.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(supplied));
    }

    internal static bool IsOpaqueDeviceKey(string? value) =>
        value is { Length: 67 } &&
        value.StartsWith("bt-", StringComparison.Ordinal) &&
        value.AsSpan(3).ToString().All(Uri.IsHexDigit);
}

internal sealed record BluetoothWorkerRequest(
    string RequestNonce,
    BluetoothDeviceKey TargetKey,
    string AdapterDeviceId,
    BluetoothWorkerOperation Operation,
    bool MutationConfirmed)
{
    internal string Serialize(string authorizationToken) =>
        JsonSerializer.Serialize(
            new RequestFrame(
                BluetoothWorkerProtocol.Version,
                authorizationToken,
                RequestNonce,
                TargetKey.Value,
                AdapterDeviceId,
                Operation.ToString(),
                MutationConfirmed),
            BluetoothWorkerProtocol.SerializerOptions);

    internal static BluetoothWorkerRequest DeserializeAndValidate(
        string json,
        string? expectedAuthorizationToken,
        BluetoothWorkerOperation expectedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RequestFrame frame = JsonSerializer.Deserialize<RequestFrame>(
            json,
            BluetoothWorkerProtocol.SerializerOptions) ??
            throw new FormatException("The Bluetooth worker request was empty.");
        bool mutation = expectedOperation is
            BluetoothWorkerOperation.Reconnect or BluetoothWorkerOperation.Disconnect;
        if (frame.ProtocolVersion != BluetoothWorkerProtocol.Version ||
            !BluetoothWorkerProtocol.IsAuthorized(expectedAuthorizationToken, frame.AuthorizationToken) ||
            !BluetoothWorkerProtocol.IsToken(frame.RequestNonce) ||
            !BluetoothWorkerProtocol.IsOpaqueDeviceKey(frame.TargetKey) ||
            !Enum.TryParse(frame.Operation, ignoreCase: false, out BluetoothWorkerOperation operation) ||
            !Enum.IsDefined(operation) ||
            operation != expectedOperation ||
            string.IsNullOrWhiteSpace(frame.AdapterDeviceId) ||
            frame.AdapterDeviceId.Length > 8192 ||
            frame.MutationConfirmed != mutation)
        {
            throw new FormatException("The Bluetooth worker request failed validation.");
        }

        return new(
            frame.RequestNonce!,
            new BluetoothDeviceKey(frame.TargetKey!),
            frame.AdapterDeviceId,
            operation,
            frame.MutationConfirmed!.Value);
    }

    private sealed record RequestFrame(
        int? ProtocolVersion,
        string? AuthorizationToken,
        string? RequestNonce,
        string? TargetKey,
        string? AdapterDeviceId,
        string? Operation,
        bool? MutationConfirmed);
}

internal sealed record BluetoothWorkerResponse(
    int HResult,
    uint BytesReturned,
    uint? SupportFlags)
{
    internal string Serialize(BluetoothWorkerRequest request) =>
        JsonSerializer.Serialize(
            new ResponseFrame(
                BluetoothWorkerProtocol.Version,
                request.RequestNonce,
                request.TargetKey.Value,
                request.Operation.ToString(),
                HResult,
                BytesReturned,
                SupportFlags),
            BluetoothWorkerProtocol.SerializerOptions);

    internal static BluetoothWorkerResponse DeserializeAndValidate(
        string json,
        BluetoothWorkerRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ResponseFrame frame = JsonSerializer.Deserialize<ResponseFrame>(
            json,
            BluetoothWorkerProtocol.SerializerOptions) ??
            throw new FormatException("The Bluetooth worker response was empty.");
        if (frame.ProtocolVersion != BluetoothWorkerProtocol.Version ||
            !string.Equals(frame.RequestNonce, request.RequestNonce, StringComparison.Ordinal) ||
            !string.Equals(frame.TargetKey, request.TargetKey.Value, StringComparison.Ordinal) ||
            !string.Equals(frame.Operation, request.Operation.ToString(), StringComparison.Ordinal) ||
            frame.HResult is null ||
            frame.BytesReturned is null)
        {
            throw new FormatException("The Bluetooth worker response failed validation.");
        }

        return new(frame.HResult.Value, frame.BytesReturned.Value, frame.SupportFlags);
    }

    private sealed record ResponseFrame(
        int? ProtocolVersion,
        string? RequestNonce,
        string? TargetKey,
        string? Operation,
        int? HResult,
        uint? BytesReturned,
        uint? SupportFlags);
}
