namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class BluetoothKsOptionsTests
{
    private const string SessionToken =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string TargetHash = "A1B2C3D4E5F60123456789AB";

    [Fact]
    public void InventoryIsReadOnlyAndAcceptsNoOperationConfirmation()
    {
        BluetoothKsOptions options = BluetoothKsOptions.Parse(["inventory"]);

        Assert.Equal(BluetoothKsCommand.Inventory, options.Command);
        Assert.Throws<ArgumentException>(() =>
            BluetoothKsOptions.Parse(["inventory", "--confirm-ks-operation"]));
        Assert.Throws<ArgumentException>(() =>
            BluetoothKsOptions.Parse(["inventory", "--session", SessionToken]));
    }

    [Fact]
    public void ProbeRequiresSessionAndExplicitDriverOperationConfirmation()
    {
        Assert.Throws<ArgumentException>(() =>
            BluetoothKsOptions.Parse(
                ["probe", "--target", TargetHash, "--confirm-ks-operation"]));
        Assert.Throws<ArgumentException>(() =>
            BluetoothKsOptions.Parse(
                ["probe", "--session", SessionToken, "--target", TargetHash]));

        BluetoothKsOptions options = BluetoothKsOptions.Parse(
        [
            "probe",
            "--session",
            SessionToken.ToLowerInvariant(),
            "--target",
            TargetHash.ToLowerInvariant(),
            "--confirm-ks-operation",
        ]);

        Assert.Equal(SessionToken, options.SessionToken);
        Assert.Equal(TargetHash, options.TargetHash);
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("disconnect")]
    public void MutationRequiresRepeatedTargetAndStoppedPlayback(string command)
    {
        string[] required =
        [
            command,
            "--session",
            SessionToken,
            "--target",
            TargetHash,
            "--confirm-target",
            TargetHash,
            "--confirm-ks-operation",
            "--confirm-playback-stopped",
        ];

        BluetoothKsOptions options = BluetoothKsOptions.Parse(required);

        Assert.Equal(TargetHash, options.ConfirmedTargetHash);
        Assert.True(options.KsOperationConfirmed);
        Assert.True(options.PlaybackStoppedConfirmed);
    }

    [Fact]
    public void MutationRejectsAConfirmationForAnotherTarget()
    {
        Assert.Throws<ArgumentException>(() => BluetoothKsOptions.Parse(
        [
            "disconnect",
            "--session",
            SessionToken,
            "--target",
            TargetHash,
            "--confirm-target",
            "00112233445566778899AABB",
            "--confirm-ks-operation",
            "--confirm-playback-stopped",
        ]));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("A1B2C3D4E5F60123456789AG")]
    public void TargetMustBeAReportScopedAlias(string value)
    {
        Assert.Throws<ArgumentException>(() => BluetoothKsOptions.Parse(
        [
            "probe",
            "--session",
            SessionToken,
            "--target",
            value,
            "--confirm-ks-operation",
        ]));
    }
}
