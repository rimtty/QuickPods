using QuickPods.Contracts;
using QuickPods.Core.Models;
using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarThemeResolverTests
{
    [Theory]
    [InlineData(QuickPodsThemeMode.System, false, false, TaskbarThemeMode.Dark)]
    [InlineData(QuickPodsThemeMode.System, true, false, TaskbarThemeMode.Light)]
    [InlineData(QuickPodsThemeMode.Dark, true, false, TaskbarThemeMode.Dark)]
    [InlineData(QuickPodsThemeMode.Light, false, false, TaskbarThemeMode.Light)]
    [InlineData(QuickPodsThemeMode.System, false, true, TaskbarThemeMode.HighContrast)]
    [InlineData(QuickPodsThemeMode.Dark, false, true, TaskbarThemeMode.HighContrast)]
    [InlineData(QuickPodsThemeMode.Light, true, true, TaskbarThemeMode.HighContrast)]
    public void ResolvesProductAndWindowsThemeForNativeHost(
        QuickPodsThemeMode productTheme,
        bool windowsAppsUseLightTheme,
        bool highContrast,
        TaskbarThemeMode expected)
    {
        Assert.Equal(
            expected,
            TaskbarThemeResolver.Resolve(
                productTheme,
                windowsAppsUseLightTheme,
                highContrast));
    }

    [Fact]
    public void RejectsUnknownProductThemeOutsideHighContrast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaskbarThemeResolver.Resolve(
                (QuickPodsThemeMode)99,
                windowsAppsUseLightTheme: false,
                highContrast: false));
    }
}
