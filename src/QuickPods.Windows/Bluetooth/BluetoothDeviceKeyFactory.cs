using System.Security.Cryptography;
using QuickPods.Core.Models;

namespace QuickPods.Windows.Bluetooth;

internal static class BluetoothDeviceKeyFactory
{
    private static ReadOnlySpan<byte> Domain => "QuickPods.Bluetooth.Container.v1\0"u8;

    internal static BluetoothDeviceKey Create(Guid containerId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(containerId, Guid.Empty);

        Span<byte> material = stackalloc byte[Domain.Length + 16];
        Domain.CopyTo(material);
        _ = containerId.TryWriteBytes(material[Domain.Length..], bigEndian: true, out _);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.TryHashData(material, digest, out _);
        return new BluetoothDeviceKey($"bt-{Convert.ToHexString(digest)}");
    }
}
