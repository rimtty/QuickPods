using QuickPods.Spike.BluetoothKs.Diagnostics;
using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Discovery;

internal sealed class MtaBluetoothKsDiscovery : IBluetoothKsDiscovery, IDisposable
{
    private readonly BluetoothKsDiscoveryService _service;
    private readonly MtaComWorker _worker = new();

    internal MtaBluetoothKsDiscovery(string? sessionToken = null)
    {
        IdentifierHasher hasher = sessionToken is null
            ? new IdentifierHasher()
            : IdentifierHasher.FromSessionToken(sessionToken);
        _service = new BluetoothKsDiscoveryService(hasher);
    }

    public BluetoothKsDiscoveryResult Discover() =>
        _worker.Invoke(_service.Discover);

    public void Dispose() => _worker.Dispose();
}
