using QuickPods.Core.Models;
using QuickPods.Windows.Bluetooth.Worker;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class BluetoothWorkerProtocolTests
{
    private static readonly BluetoothDeviceKey Target = new($"bt-{new string('A', 64)}");

    [Fact]
    public void AuthorizedProbeRequestRoundTripsWithStrictOperationBinding()
    {
        string token = BluetoothWorkerProtocol.CreateToken();
        var request = new BluetoothWorkerRequest(
            BluetoothWorkerProtocol.CreateToken(),
            Target,
            "synthetic-adapter-id",
            BluetoothWorkerOperation.BasicSupportReconnect,
            MutationConfirmed: false);

        BluetoothWorkerRequest parsed = BluetoothWorkerRequest.DeserializeAndValidate(
            request.Serialize(token),
            token,
            BluetoothWorkerOperation.BasicSupportReconnect);

        Assert.Equal(request, parsed);
    }

    [Fact]
    public void MutationRequiresTheExplicitMutationCapability()
    {
        string token = BluetoothWorkerProtocol.CreateToken();
        var request = new BluetoothWorkerRequest(
            BluetoothWorkerProtocol.CreateToken(),
            Target,
            "synthetic-adapter-id",
            BluetoothWorkerOperation.Disconnect,
            MutationConfirmed: false);

        Assert.Throws<FormatException>(() => BluetoothWorkerRequest.DeserializeAndValidate(
            request.Serialize(token),
            token,
            BluetoothWorkerOperation.Disconnect));
    }

    [Fact]
    public void ResponseFromAnotherRequestIsRejected()
    {
        var expected = new BluetoothWorkerRequest(
            BluetoothWorkerProtocol.CreateToken(),
            Target,
            "synthetic-adapter-id",
            BluetoothWorkerOperation.BasicSupportDisconnect,
            MutationConfirmed: false);
        var other = expected with { RequestNonce = BluetoothWorkerProtocol.CreateToken() };
        string response = new BluetoothWorkerResponse(0, sizeof(uint), 1).Serialize(other);

        Assert.Throws<FormatException>(() =>
            BluetoothWorkerResponse.DeserializeAndValidate(response, expected));
    }
}
