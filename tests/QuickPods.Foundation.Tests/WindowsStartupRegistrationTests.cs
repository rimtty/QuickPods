using System.IO;
using QuickPods.Windows.Startup;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public async Task EnableQueryAndDisableAreIdempotentAndTargetOnlyMainExecutable()
    {
        string executable = Path.Combine(
            Path.GetTempPath(),
            "QuickPods Startup Test",
            "QuickPods.exe");
        var registry = new FakeStartupRegistry();
        var registration = new WindowsStartupRegistration(executable, registry);

        Assert.False(await registration.IsEnabledAsync());

        await registration.SetEnabledAsync(true);
        await registration.SetEnabledAsync(true);

        Assert.True(await registration.IsEnabledAsync());
        Assert.Equal(
            $"\"{Path.GetFullPath(executable)}\" --background",
            registry.Command);
        Assert.Equal(1, registry.WriteCount);

        await registration.SetEnabledAsync(false);
        await registration.SetEnabledAsync(false);

        Assert.False(await registration.IsEnabledAsync());
        Assert.Null(registry.Command);
        Assert.Equal(1, registry.DeleteCount);
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        internal string? Command { get; private set; }

        internal int WriteCount { get; private set; }

        internal int DeleteCount { get; private set; }

        public string? ReadCommand(string valueName)
        {
            Assert.Equal(WindowsStartupRegistration.ValueName, valueName);
            return Command;
        }

        public void WriteCommand(string valueName, string command)
        {
            Assert.Equal(WindowsStartupRegistration.ValueName, valueName);
            Command = command;
            WriteCount++;
        }

        public void DeleteCommand(string valueName)
        {
            Assert.Equal(WindowsStartupRegistration.ValueName, valueName);
            Command = null;
            DeleteCount++;
        }
    }
}
