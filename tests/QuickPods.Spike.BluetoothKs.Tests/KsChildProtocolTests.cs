using QuickPods.Spike.BluetoothKs.Runtime;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class KsChildProtocolTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF";
    private const string TargetHash = "A1B2C3D4E5F60123456789AB";

    [Fact]
    public void ResponseRoundTripsOnlyForTheExpectedInvocation()
    {
        var expected = new KsChildResponse(
            HResult: 0,
            BytesReturned: 4,
            SupportFlags: 1);

        string json = expected.Serialize(
            Nonce,
            TargetHash,
            KsChildOperation.BasicSupportReconnect);
        KsChildResponse observed = KsChildResponse.Deserialize(
            json,
            Nonce,
            TargetHash,
            KsChildOperation.BasicSupportReconnect);

        Assert.Equal(expected, observed);
        Assert.DoesNotContain("device", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adapter", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<FormatException>(() => KsChildResponse.Deserialize(
            json,
            Nonce,
            TargetHash,
            KsChildOperation.BasicSupportDisconnect));
    }

    [Fact]
    public void EmptyResponseIsRejected()
    {
        Assert.Throws<ArgumentException>(() => KsChildResponse.Deserialize(
            string.Empty,
            Nonce,
            TargetHash,
            KsChildOperation.BasicSupportReconnect));
    }

    [Fact]
    public void MissingResponseFieldsAreRejected()
    {
        Assert.Throws<FormatException>(() => KsChildResponse.Deserialize(
            "{}",
            Nonce,
            TargetHash,
            KsChildOperation.BasicSupportReconnect));
    }

    [Fact]
    public void ChildAuthorizationRequiresThePerLaunchToken()
    {
        const string expected = "0123456789ABCDEF0123456789ABCDEF";

        Assert.True(KsChildProtocol.IsAuthorized(expected, expected));
        Assert.False(KsChildProtocol.IsAuthorized(expected, null));
        Assert.False(KsChildProtocol.IsAuthorized(
            expected,
            "0123456789ABCDEF0123456789ABCDE0"));
    }

    [Fact]
    public void MutationCapabilityRequiresPlaybackAndDriverConsent()
    {
        const string authorization = "FEDCBA9876543210FEDCBA9876543210";
        var invalid = new KsChildRequest(
            Nonce,
            TargetHash,
            "raw-input-remains-in-memory-only",
            KsChildOperation.Reconnect,
            KsChildConsent.Probe);

        Assert.Throws<FormatException>(() => KsChildRequest.DeserializeAndValidate(
            invalid.Serialize(authorization),
            authorization,
            KsChildOperation.Reconnect));
    }

    [Fact]
    public void UnauthorizedChildRefusesBeforeUsingADeviceIdentifier()
    {
        const string expectedAuthorization = "0123456789ABCDEF0123456789ABCDEF";
        const string suppliedAuthorization = "FEDCBA9876543210FEDCBA9876543210";
        var request = new KsChildRequest(
            Nonce,
            TargetHash,
            "raw-input-remains-in-memory-only",
            KsChildOperation.Reconnect,
            KsChildConsent.Mutation);
        using var input = new StringReader(request.Serialize(suppliedAuthorization));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = KsChildEntryPoint.Run(
            KsChildOperation.Reconnect,
            expectedAuthorization,
            sameExecutableParent: true,
            input,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("capability validation failed", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "raw-input",
            error.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealKsChildRejectsAnOtherwiseValidCapabilityFromAnotherExecutable()
    {
        const string authorization = "0123456789ABCDEF0123456789ABCDEF";
        var request = new KsChildRequest(
            Nonce,
            TargetHash,
            "raw-input-remains-in-memory-only",
            KsChildOperation.Disconnect,
            KsChildConsent.Mutation);
        using var input = new StringReader(request.Serialize(authorization));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = KsChildEntryPoint.Run(
            KsChildOperation.Disconnect,
            authorization,
            sameExecutableParent: false,
            input,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("parent validation failed", error.ToString(), StringComparison.Ordinal);
    }
}
