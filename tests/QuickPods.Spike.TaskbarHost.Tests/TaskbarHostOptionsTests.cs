namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarHostOptionsTests
{
    [Theory]
    [InlineData()]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_HelpInputs_ReturnHelp(params string[] args)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(args);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskbarCommand.Help, result.Options!.Command);
    }

    [Fact]
    public void Parse_Inspect_IsReadOnlyAndNeedsNoConfirmation()
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(["inspect"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskbarCommand.Inspect, result.Options!.Command);
        Assert.False(result.Options.LiveHostConfirmed);
    }

    [Theory]
    [InlineData("child", "Child")]
    [InlineData("popup", "Popup")]
    public void Parse_ConfirmedHost_ReturnsBoundedOptions(string value, string expected)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(
            ["host", "--style", value, "--duration", "30", "--confirm-live-host"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskbarCommand.Host, result.Options!.Command);
        Assert.Equal(expected, result.Options.Style.ToString());
        Assert.Equal(TimeSpan.FromSeconds(30), result.Options.Duration);
        Assert.True(result.Options.LiveHostConfirmed);
    }

    [Fact]
    public void Parse_HostWithoutConfirmation_FailsClosed()
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(["host"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("requires --confirm-live-host", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("1.5")]
    [InlineData("not-a-number")]
    public void Parse_InvalidDuration_IsRejected(string value)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(
            ["host", "--duration", value, "--confirm-live-host"]);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("inspect", "--confirm-live-host")]
    [InlineData("host", "--confirm-live-host", "--confirm-live-host")]
    [InlineData("host", "--style", "floating", "--confirm-live-host")]
    [InlineData("host", "--unknown", "value", "--confirm-live-host")]
    [InlineData("unknown")]
    public void Parse_UnknownOrContradictoryInput_IsRejected(params string[] args)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(args);

        Assert.False(result.IsSuccess);
    }
}
