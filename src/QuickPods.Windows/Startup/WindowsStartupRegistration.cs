using QuickPods.Core.Ports;

namespace QuickPods.Windows.Startup;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    public const string ValueName = "QuickPods";

    private readonly IStartupRegistry registry;
    private readonly string expectedCommand;

    public WindowsStartupRegistration(
        string executablePath,
        IStartupRegistry? registry = null)
    {
        expectedCommand = StartupRegistrationCommand.Create(executablePath);
        this.registry = registry ?? new WindowsCurrentUserStartupRegistry();
    }

    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? currentCommand = registry.ReadCommand(ValueName);
        return ValueTask.FromResult(string.Equals(
            currentCommand,
            expectedCommand,
            StringComparison.OrdinalIgnoreCase));
    }

    public ValueTask SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? currentCommand = registry.ReadCommand(ValueName);
        if (enabled)
        {
            if (!string.Equals(
                currentCommand,
                expectedCommand,
                StringComparison.OrdinalIgnoreCase))
            {
                registry.WriteCommand(ValueName, expectedCommand);
            }
        }
        else if (currentCommand is not null)
        {
            registry.DeleteCommand(ValueName);
        }

        return ValueTask.CompletedTask;
    }
}
