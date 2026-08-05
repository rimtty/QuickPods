using QuickPods.Spike.BluetoothKs.Diagnostics;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class IdentifierHasherTests
{
    [Fact]
    public void ReportSessionCanBeReusedWithoutPersistingDeviceIdentity()
    {
        const string source = "private-device-identifier";
        var firstReport = new IdentifierHasher();
        var resumedReport = IdentifierHasher.FromSessionToken(
            firstReport.SessionToken);

        string first = firstReport.Hash(source);
        string resumed = resumedReport.Hash(source);

        Assert.Equal(first, resumed);
        Assert.Equal(24, first.Length);
        Assert.Equal(64, firstReport.SessionToken.Length);
        Assert.DoesNotContain(source, first, StringComparison.Ordinal);
    }

    [Fact]
    public void SeparateReportSessionsCannotBeLinkedByTheirAliases()
    {
        const string source = "private-device-identifier";

        var firstReport = new IdentifierHasher();
        var secondReport = new IdentifierHasher();

        Assert.NotEqual(firstReport.SessionToken, secondReport.SessionToken);
        Assert.NotEqual(firstReport.Hash(source), secondReport.Hash(source));
    }

    [Fact]
    public void InvalidSessionTokenIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            IdentifierHasher.FromSessionToken("not-a-session"));
    }
}
