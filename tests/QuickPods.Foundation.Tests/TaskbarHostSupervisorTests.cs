using QuickPods.Infrastructure.Runtime;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarHostSupervisorTests
{
    [Fact]
    public void RepeatedFailureDisablesHostWithoutStoppingMainApplication()
    {
        var supervisor = new TaskbarHostSupervisor(failureLimit: 2);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        supervisor.RecordUnexpectedExit(now);
        Assert.Equal(TaskbarHostLifecycle.BackingOff, supervisor.State.Lifecycle);

        supervisor.RecordUnexpectedExit(now);
        Assert.Equal(TaskbarHostLifecycle.DisabledForSession, supervisor.State.Lifecycle);
        Assert.False(supervisor.CanRestart(now + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ReconnectPreservesFailureCountUntilConnectionIsStable()
    {
        var supervisor = new TaskbarHostSupervisor(failureLimit: 3);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        supervisor.RecordUnexpectedExit(now);
        supervisor.RecordStart();
        supervisor.RecordConnected();

        Assert.Equal(TaskbarHostLifecycle.Connected, supervisor.State.Lifecycle);
        Assert.Equal(1, supervisor.State.ConsecutiveFailures);

        supervisor.RecordStable();

        Assert.Equal(0, supervisor.State.ConsecutiveFailures);
    }
}
