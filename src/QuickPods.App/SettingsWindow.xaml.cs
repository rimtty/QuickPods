using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPods.Core.Models;
using QuickPods.Presentation;
using WpfClipboard = System.Windows.Clipboard;

namespace QuickPods.App;

public partial class SettingsWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int DwmWindowCornerRound = 2;
    private const int DwmSystemBackdropMainWindow = 2;

    private readonly Func<QuickPodsSettings> readSettings;
    private readonly Func<QuickPodsSettings, Task<QuickPodsSettings>> updateSettings;
    private readonly Func<bool, Task<QuickPodsSettings>> updateStartup;
    private readonly Action openLogs;
    private readonly Func<string> createDiagnosticSummary;
    private readonly Action restartApplication;
    private readonly ProductLocalizer localizer;
    private bool applyingSettings;
    private bool updatePending;

    internal SettingsWindow(
        Func<QuickPodsSettings> readSettings,
        Func<QuickPodsSettings, Task<QuickPodsSettings>> updateSettings,
        Func<bool, Task<QuickPodsSettings>> updateStartup,
        Action openLogs,
        Func<string> createDiagnosticSummary,
        Action restartApplication,
        ProductLocalizer localizer,
        string version)
    {
        this.readSettings = readSettings ?? throw new ArgumentNullException(nameof(readSettings));
        this.updateSettings = updateSettings ??
            throw new ArgumentNullException(nameof(updateSettings));
        this.updateStartup = updateStartup ??
            throw new ArgumentNullException(nameof(updateStartup));
        this.openLogs = openLogs ?? throw new ArgumentNullException(nameof(openLogs));
        this.createDiagnosticSummary = createDiagnosticSummary ??
            throw new ArgumentNullException(nameof(createDiagnosticSummary));
        this.restartApplication = restartApplication ??
            throw new ArgumentNullException(nameof(restartApplication));
        this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        InitializeComponent();
        InitializeChoices();
        VersionText.Text = $"QuickPods {version}";
        ApplySettings(readSettings());
    }

    internal void ApplySettings(QuickPodsSettings settings)
    {
        applyingSettings = true;
        try
        {
            QuickPodsSettings normalized = settings.Normalize();
            DisplayModeComboBox.SelectedValue = normalized.DisplayMode;
            ThemeComboBox.SelectedValue = normalized.Theme;
            LanguageComboBox.SelectedValue = normalized.Language;
            MouseWheelStepComboBox.SelectedValue = normalized.MouseWheelStepPercent;
            SetConnectedDeviceAsDefaultCheckBox.IsChecked =
                normalized.SetConnectedDeviceAsDefault;
            ConfirmBluetoothDisconnectCheckBox.IsChecked =
                normalized.ConfirmBluetoothDisconnect;
            StartWithWindowsCheckBox.IsChecked = normalized.StartWithWindows;
            ApplyNativeWindowTheme();
        }
        finally
        {
            applyingSettings = false;
        }
    }

    internal void ReportDiagnostic(string message) => DiagnosticText.Text = message ?? string.Empty;

    internal void ApplyLocalization()
    {
        InitializeChoices();
        ApplySettings(readSettings());
    }

    private async void OnProductSettingChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings || updatePending ||
            DisplayModeComboBox.SelectedValue is not QuickPodsDisplayMode displayMode ||
            ThemeComboBox.SelectedValue is not QuickPodsThemeMode theme ||
            LanguageComboBox.SelectedValue is not QuickPodsLanguageMode language ||
            MouseWheelStepComboBox.SelectedValue is not int wheelStep)
        {
            return;
        }

        QuickPodsSettings current = readSettings();
        QuickPodsSettings requested = current with
        {
            DisplayMode = displayMode,
            Theme = theme,
            Language = language,
            MouseWheelStepPercent = wheelStep,
            SetConnectedDeviceAsDefault = SetConnectedDeviceAsDefaultCheckBox.IsChecked == true,
            ConfirmBluetoothDisconnect = ConfirmBluetoothDisconnectCheckBox.IsChecked == true,
        };
        await RunUpdateAsync(
            () => updateSettings(requested),
            localizer["SettingsSaveActionFailed"]);
    }

    private async void OnStartWithWindowsChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings || updatePending)
        {
            return;
        }

        bool requested = StartWithWindowsCheckBox.IsChecked == true;
        await RunUpdateAsync(
            () => updateStartup(requested),
            localizer["StartupChangeFailed"]);
    }

    private void OnOpenLogs(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            openLogs();
            ReportDiagnostic(string.Empty);
        }
        catch (Exception exception)
        {
            ReportDiagnostic(localizer.Format("LogOpenFailed", exception.Message));
        }
    }

    private void OnCopyDiagnostics(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            WpfClipboard.SetText(createDiagnosticSummary());
            ReportDiagnostic(localizer["DiagnosticsCopied"]);
        }
        catch (Exception exception)
        {
            ReportDiagnostic(localizer.Format("DiagnosticsCopyFailed", exception.Message));
        }
    }

    private async void OnResetSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings || updatePending)
        {
            return;
        }

        await RunUpdateAsync(
            ResetEditableSettingsAsync,
            localizer["ResetFailed"]);
    }

    private void OnRestartApplication(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings || updatePending)
        {
            return;
        }

        ReportDiagnostic(string.Empty);
        try
        {
            restartApplication();
        }
        catch (Exception exception)
        {
            ReportDiagnostic(localizer.Format("RestartFailed", exception.Message));
        }
    }

    private void OnCloseWindow(object sender, RoutedEventArgs eventArgs) => Close();

    private async Task<QuickPodsSettings> ResetEditableSettingsAsync()
    {
        QuickPodsSettings defaults = QuickPodsSettings.Default;
        QuickPodsSettings current = readSettings();
        if (current.StartWithWindows != defaults.StartWithWindows)
        {
            current = await updateStartup(defaults.StartWithWindows);
        }

        return await updateSettings(current with
        {
            DisplayMode = defaults.DisplayMode,
            Theme = defaults.Theme,
            Language = defaults.Language,
            MouseWheelStepPercent = defaults.MouseWheelStepPercent,
            SetConnectedDeviceAsDefault = defaults.SetConnectedDeviceAsDefault,
            ConfirmBluetoothDisconnect = defaults.ConfirmBluetoothDisconnect,
        });
    }

    private async Task RunUpdateAsync(
        Func<Task<QuickPodsSettings>> operation,
        string failureMessage)
    {
        updatePending = true;
        SetSettingsControlsEnabled(false);
        ReportDiagnostic(string.Empty);
        try
        {
            ApplySettings(await operation());
        }
        catch (Exception exception)
        {
            ApplySettings(readSettings());
            ReportDiagnostic($"{failureMessage}: {exception.Message}");
        }
        finally
        {
            updatePending = false;
            SetSettingsControlsEnabled(true);
        }
    }

    private void SetSettingsControlsEnabled(bool enabled)
    {
        DisplayModeComboBox.IsEnabled = enabled;
        ThemeComboBox.IsEnabled = enabled;
        LanguageComboBox.IsEnabled = enabled;
        MouseWheelStepComboBox.IsEnabled = enabled;
        SetConnectedDeviceAsDefaultCheckBox.IsEnabled = enabled;
        ConfirmBluetoothDisconnectCheckBox.IsEnabled = enabled;
        StartWithWindowsCheckBox.IsEnabled = enabled;
        ResetSettingsButton.IsEnabled = enabled;
        RestartApplicationButton.IsEnabled = enabled;
    }

    private void InitializeChoices()
    {
        applyingSettings = true;
        try
        {
            DisplayModeComboBox.ItemsSource = new SettingChoice<QuickPodsDisplayMode>[]
            {
                new(QuickPodsDisplayMode.Auto, localizer["ChoiceAuto"]),
                new(QuickPodsDisplayMode.TrayOnly, localizer["ChoiceTrayOnly"]),
            };
            ThemeComboBox.ItemsSource = new SettingChoice<QuickPodsThemeMode>[]
            {
                new(QuickPodsThemeMode.System, localizer["ChoiceSystemTheme"]),
                new(QuickPodsThemeMode.Dark, localizer["ChoiceDark"]),
                new(QuickPodsThemeMode.Light, localizer["ChoiceLight"]),
            };
            LanguageComboBox.ItemsSource = new SettingChoice<QuickPodsLanguageMode>[]
            {
                new(QuickPodsLanguageMode.System, localizer["ChoiceSystemLanguage"]),
                new(QuickPodsLanguageMode.English, localizer["ChoiceEnglish"]),
                new(QuickPodsLanguageMode.Japanese, localizer["ChoiceJapanese"]),
            };
            MouseWheelStepComboBox.ItemsSource = new SettingChoice<int>[]
            {
                new(1, "1%"),
                new(2, "2%"),
                new(5, "5%"),
                new(10, "10%"),
            };
        }
        finally
        {
            applyingSettings = false;
        }
    }

    private sealed record SettingChoice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs) =>
        ApplyNativeWindowTheme();

    private void ApplyNativeWindowTheme()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        nint windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == nint.Zero)
        {
            return;
        }

        bool useDarkTitleBar = System.Windows.Application.Current.Resources["WindowBrush"] is SolidColorBrush brush &&
            ((brush.Color.R * 299) + (brush.Color.G * 587) + (brush.Color.B * 114)) < 128000;
        int darkMode = useDarkTitleBar ? 1 : 0;
        int cornerPreference = DwmWindowCornerRound;
        int backdropType = DwmSystemBackdropMainWindow;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmUseImmersiveDarkMode,
            ref darkMode,
            sizeof(int));
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmSystemBackdropType,
            ref backdropType,
            sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
