using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class IsolatedCommandProtocolTests
{
    private const string Authorization = "0123456789ABCDEF0123456789ABCDEF";
    private const string Nonce = "FEDCBA9876543210FEDCBA9876543210";

    [Fact]
    public void CommandCapabilityIsBoundToTheSameExecutableParent()
    {
        var request = new IsolatedCommandRequest(Nonce, ["inventory"]);
        string json = request.Serialize(Authorization);

        IsolatedCommandRequest accepted =
            IsolatedCommandRequest.DeserializeAndValidate(
                json,
                Authorization,
                sameExecutableParent: true);

        Assert.Equal(request.RequestNonce, accepted.RequestNonce);
        Assert.Equal(request.Arguments, accepted.Arguments);
        Assert.Throws<FormatException>(() =>
            IsolatedCommandRequest.DeserializeAndValidate(
                json,
                Authorization,
                sameExecutableParent: false));
    }

    [Fact]
    public void CommandResponseRequiresTheExpectedNonceAndAllFields()
    {
        var response = new IsolatedCommandResponse(
            ExitCode: 0,
            StandardOutput: "sanitized output",
            StandardError: string.Empty);
        string json = response.Serialize(Nonce);

        Assert.Equal(response, IsolatedCommandResponse.Deserialize(json, Nonce));
        Assert.Throws<FormatException>(() =>
            IsolatedCommandResponse.Deserialize(json, Authorization));
        Assert.Throws<FormatException>(() =>
            IsolatedCommandResponse.Deserialize("{}", Nonce));
    }
}
