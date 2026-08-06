using System.Security.Cryptography;
using System.Text;

namespace QuickPods.Spike.DefaultEndpointPolicy;

internal sealed class EndpointAliasSession
{
    private const int KeyLength = 32;
    private const int AliasLength = 12;

    private readonly byte[] _key;

    internal EndpointAliasSession()
        : this(RandomNumberGenerator.GetBytes(KeyLength))
    {
    }

    private EndpointAliasSession(byte[] key)
    {
        _key = key;
    }

    internal string Token => Convert.ToHexString(_key);

    internal static EndpointAliasSession FromToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length != KeyLength * 2 || token.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The inventory session token must contain 64 hexadecimal characters.",
                nameof(token));
        }

        return new EndpointAliasSession(Convert.FromHexString(token));
    }

    internal string Alias(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            byte[] hash = HMACSHA256.HashData(_key, bytes);
            return Convert.ToHexString(hash.AsSpan(0, AliasLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
