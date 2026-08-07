using QuickPods.Contracts;
using QuickPods.Core.Models;

namespace QuickPods.Presentation;

public static class TaskbarThemeResolver
{
    public static TaskbarThemeMode Resolve(
        QuickPodsThemeMode productTheme,
        bool windowsAppsUseLightTheme,
        bool highContrast)
    {
        if (highContrast)
        {
            return TaskbarThemeMode.HighContrast;
        }

        return productTheme switch
        {
            QuickPodsThemeMode.System => windowsAppsUseLightTheme
                ? TaskbarThemeMode.Light
                : TaskbarThemeMode.Dark,
            QuickPodsThemeMode.Dark => TaskbarThemeMode.Dark,
            QuickPodsThemeMode.Light => TaskbarThemeMode.Light,
            _ => throw new ArgumentOutOfRangeException(
                nameof(productTheme),
                productTheme,
                null),
        };
    }
}
