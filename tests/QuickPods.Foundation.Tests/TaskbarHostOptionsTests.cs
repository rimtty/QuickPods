using QuickPods.TaskbarHost;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarHostOptionsTests
{
    [Fact]
    public void PreviewRequiresExplicitConfirmationAndBoundedDuration()
    {
        Assert.Equal(
            TaskbarHostCommand.Invalid,
            TaskbarHostOptions.Parse(["preview", "--duration-seconds", "15"]).Command);
        Assert.Equal(
            TaskbarHostCommand.Invalid,
            TaskbarHostOptions.Parse(["preview", "--confirm-live-host", "--duration-seconds", "61"]).Command);

        TaskbarHostOptions valid = TaskbarHostOptions.Parse(
            ["preview", "--confirm-live-host", "--duration-seconds", "15"]);
        Assert.Equal(TaskbarHostCommand.Preview, valid.Command);
        Assert.Equal(TimeSpan.FromSeconds(15), valid.PreviewDuration);
    }

    [Fact]
    public void RunRequiresBoundedPipeNameAndPositiveParentProcess()
    {
        Assert.Equal(
            TaskbarHostCommand.Invalid,
            TaskbarHostOptions.Parse(["run", "--pipe-name", "short", "--parent-pid", "1"]).Command);

        TaskbarHostOptions valid = TaskbarHostOptions.Parse(
            ["run", "--pipe-name", "QuickPods.0123456789abcdef", "--parent-pid", "42"]);

        Assert.Equal(TaskbarHostCommand.Run, valid.Command);
        Assert.Equal("QuickPods.0123456789abcdef", valid.PipeName);
        Assert.Equal(42, valid.ParentProcessId);
    }
}
