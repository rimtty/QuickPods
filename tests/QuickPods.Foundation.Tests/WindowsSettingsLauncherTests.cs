using System.ComponentModel;
using QuickPods.Windows.Settings;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class WindowsSettingsLauncherTests
{
    [Fact]
    public void ShellUriDispatchUsesExceptionRatherThanProcessInstanceAsResult()
    {
        Assert.True(WindowsSettingsLauncher.TryOpen(
            "ms-settings:bluetooth",
            _ => null));
        Assert.False(WindowsSettingsLauncher.TryOpen(
            "ms-settings:bluetooth",
            _ => throw new Win32Exception()));
    }
}
