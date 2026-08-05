using System.Security.Cryptography;
using System.Text;

namespace QuickPods.Spike.BluetoothKs.Diagnostics;

internal sealed class IdentifierHasher
{
    private const int SessionKeyLength = 32;
    private const int AliasByteLength = 12;

    private readonly byte[] _sessionKey;

    public IdentifierHasher()
        : this(RandomNumberGenerator.GetBytes(SessionKeyLength))
    {
    }

    internal IdentifierHasher(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != SessionKeyLength)
        {
            throw new ArgumentException(
                $"A {SessionKeyLength}-byte report session key is required.",
                nameof(sessionKey));
        }

        _sessionKey = sessionKey.ToArray();
    }

    public string SessionToken => Convert.ToHexString(_sessionKey);

    internal static IdentifierHasher FromSessionToken(string sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        if (sessionToken.Length != SessionKeyLength * 2 ||
            sessionToken.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The report session token must be 64 hexadecimal characters.",
                nameof(sessionToken));
        }

        return new IdentifierHasher(Convert.FromHexString(sessionToken));
    }

    public string Hash(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        byte[] identifierBytes = Encoding.UTF8.GetBytes(identifier);
        byte[] hash = HMACSHA256.HashData(_sessionKey, identifierBytes);
        CryptographicOperations.ZeroMemory(identifierBytes);
        return Convert.ToHexString(hash.AsSpan(0, AliasByteLength));
    }
}

internal static class DeviceLabelSanitizer
{
    public static string Classify(string? friendlyName)
    {
        if (friendlyName?.Contains("AirPods", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "AirPods audio";
        }

        if (friendlyName?.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Bluetooth audio";
        }

        return "Audio endpoint";
    }
}
