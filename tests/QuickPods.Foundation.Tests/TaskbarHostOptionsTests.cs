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
}
