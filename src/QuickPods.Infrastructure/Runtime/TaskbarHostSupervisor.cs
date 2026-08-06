namespace QuickPods.Infrastructure.Runtime;

public enum TaskbarHostLifecycle
{
    Stopped,
    Starting,
    Connected,
    BackingOff,
    DisabledForSession,
}

public sealed record TaskbarHostSupervisorState(
    TaskbarHostLifecycle Lifecycle,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAfter);

public sealed class TaskbarHostSupervisor
{
    private readonly int failureLimit;

    public TaskbarHostSupervisor(int failureLimit = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failureLimit, 1);

        this.failureLimit = failureLimit;
    }

    public TaskbarHostSupervisorState State { get; private set; } =
        new(TaskbarHostLifecycle.Stopped, 0, null);

    public void RecordStart() =>
        State = State with { Lifecycle = TaskbarHostLifecycle.Starting, RetryAfter = null };

    public void RecordConnected() =>
        State = State with { Lifecycle = TaskbarHostLifecycle.Connected, RetryAfter = null };

    public void RecordStable() =>
        State = new TaskbarHostSupervisorState(TaskbarHostLifecycle.Connected, 0, null);

    public void RecordStopped() =>
        State = new TaskbarHostSupervisorState(TaskbarHostLifecycle.Stopped, 0, null);

    public void RecordUnexpectedExit(DateTimeOffset now)
    {
        int failures = State.ConsecutiveFailures + 1;
        State = failures >= failureLimit
            ? new TaskbarHostSupervisorState(TaskbarHostLifecycle.DisabledForSession, failures, null)
            : new TaskbarHostSupervisorState(
                TaskbarHostLifecycle.BackingOff,
                failures,
                now + TimeSpan.FromSeconds(Math.Min(8, 1 << (failures - 1))));
    }

    public bool CanRestart(DateTimeOffset now) =>
        State.Lifecycle == TaskbarHostLifecycle.BackingOff && State.RetryAfter <= now;
}
