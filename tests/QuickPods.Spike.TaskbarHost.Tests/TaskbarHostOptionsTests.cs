namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarHostOptionsTests
{
    [Theory]
    [InlineData("--help")]
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
        Assert.Equal(RequestedFallbackMode.Floating, result.Options.Fallback);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Options.Duration);
        Assert.True(result.Options.LiveHostConfirmed);
    }

    [Fact]
    public void Parse_ConfirmedHostWithoutStyle_DefaultsToPopup()
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(
            ["host", "--confirm-live-host"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequestedHostStyle.Popup, result.Options!.Style);
    }

    [Theory]
    [InlineData("hidden", (int)RequestedFallbackMode.Hidden)]
    public void Parse_ExplicitFallback_ReturnsRequestedMode(
        string value,
        int expectedValue)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(
            ["host", "--fallback", value, "--confirm-live-host"]);

        Assert.True(result.IsSuccess);
        Assert.Equal((RequestedFallbackMode)expectedValue, result.Options!.Fallback);
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
    public void Parse_InvalidDuration_IsRejected(string value)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(
            ["host", "--duration", value, "--confirm-live-host"]);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("host", "--unknown", "value", "--confirm-live-host")]
    public void Parse_UnknownOrContradictoryInput_IsRejected(params string[] args)
    {
        OptionsParseResult result = TaskbarHostOptions.Parse(args);

        Assert.False(result.IsSuccess);
    }
}
