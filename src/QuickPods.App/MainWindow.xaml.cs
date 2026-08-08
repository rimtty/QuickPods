using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickPods.Contracts;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Settings;
using QuickPods.Presentation;
using QuickPods.Windows.Settings;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;

namespace QuickPods.App;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;

    private readonly AudioController audio;
    private readonly BluetoothCatalogController? bluetoothCatalog;
    private readonly BluetoothOperationController? bluetoothOperations;
    private readonly IWindowsSettingsLauncher settingsLauncher;
    private readonly JsonBluetoothSelectionStore settingsStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly ProductLifetimePolicy lifetimePolicy;
    private readonly Action restartApplication;
    private readonly Func<ApplicationUpdateViewState> readUpdateState;
    private readonly Func<Task<ApplicationUpdateViewState>> checkForUpdates;
    private readonly Action<Uri> openReleasePage;
    private readonly ProductLocalizer localizer;
    private readonly string logsDirectory;
    private bool applyingAudioState;
    private bool applyingBluetoothState;
    private bool bluetoothRefreshing;
    private string? bluetoothCatalogError;
    private BluetoothProductPresentation bluetoothView;
    private QuickPodsSettings productSettings = QuickPodsSettings.Default;
    private TaskbarSurfaceAnchor? placementAnchor;
    private bool placementRefreshPending;
    private SettingsWindow? settingsWindow;
    private string settingsDiagnostic = string.Empty;
    private Task? initialization;

    internal event Action<QuickPodsSettings>? SettingsChanged;

    internal event EventHandler? FlyoutPointerEntered;

    internal event EventHandler? FlyoutPointerExited;

    internal MainWindow(
        AudioController audio,
        BluetoothCatalogController? bluetoothCatalog,
        BluetoothOperationController? bluetoothOperations,
        IWindowsSettingsLauncher settingsLauncher,
        JsonBluetoothSelectionStore settingsStore,
        IStartupRegistration startupRegistration,
        ProductLifetimePolicy lifetimePolicy,
        Action restartApplication,
        Func<ApplicationUpdateViewState> readUpdateState,
        Func<Task<ApplicationUpdateViewState>> checkForUpdates,
        Action<Uri> openReleasePage,
        ProductLocalizer localizer,
        string logsDirectory,
        string? startupDiagnostic)
    {
        this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
        this.bluetoothCatalog = bluetoothCatalog;
        this.bluetoothOperations = bluetoothOperations;
        this.settingsLauncher = settingsLauncher ??
            throw new ArgumentNullException(nameof(settingsLauncher));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.startupRegistration = startupRegistration ??
            throw new ArgumentNullException(nameof(startupRegistration));
        this.lifetimePolicy = lifetimePolicy ??
            throw new ArgumentNullException(nameof(lifetimePolicy));
        this.restartApplication = restartApplication ??
            throw new ArgumentNullException(nameof(restartApplication));
        this.readUpdateState = readUpdateState ??
            throw new ArgumentNullException(nameof(readUpdateState));
        this.checkForUpdates = checkForUpdates ??
            throw new ArgumentNullException(nameof(checkForUpdates));
        this.openReleasePage = openReleasePage ??
            throw new ArgumentNullException(nameof(openReleasePage));
        this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        this.logsDirectory = Path.GetFullPath(logsDirectory);
        bluetoothRefreshing = bluetoothCatalog is not null;
        bluetoothCatalogError = bluetoothCatalog is null
            ? localizer["BluetoothServiceInitFailed"]
            : null;
        bluetoothView = BluetoothProductPresenter.Project(
            bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty,
            bluetoothOperations?.State ?? BluetoothOperationSnapshot.Idle,
            bluetoothRefreshing,
            bluetoothCatalogError,
            localizer);

        InitializeComponent();
        AudioDiagnosticText.Text = startupDiagnostic ?? string.Empty;
        audio.StateChanged += OnAudioStateChanged;
        if (bluetoothCatalog is not null)
        {
            bluetoothCatalog.StateChanged += OnBluetoothCatalogStateChanged;
        }

        if (bluetoothOperations is not null)
        {
            bluetoothOperations.StateChanged += OnBluetoothOperationStateChanged;
        }

        ApplyBluetoothPresentation();
    }

    internal void ReportBluetoothCatalogFailure(string message)
    {
        bluetoothRefreshing = false;
        bluetoothCatalogError = string.IsNullOrWhiteSpace(message)
            ? localizer["CatalogRefreshFailed"]
            : localizer.Format("CatalogRefreshFailedWithDetail", message);
        ApplyBluetoothPresentation();
    }

    internal void ReportSettingsFailure(string message)
    {
        settingsDiagnostic = message ?? string.Empty;
        SettingsDiagnosticText.Text = settingsDiagnostic;
        settingsWindow?.ReportDiagnostic(settingsDiagnostic);
    }

    internal void ReportUpdateCheckState(ApplicationUpdateViewState state) =>
        settingsWindow?.ApplyUpdateState(state);

    internal void ApplyLocalization()
    {
        settingsWindow?.ApplyLocalization();
        settingsWindow?.ApplyUpdateState(readUpdateState());
        ApplyAudioState(audio.State);
        ApplyBluetoothPresentation();
    }

    internal void ShowSettingsWindow()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(
                () => productSettings,
                UpdateProductSettingsAsync,
                UpdateStartupSettingAsync,
                OpenLogs,
                CreateDiagnosticSummary,
                restartApplication,
                readUpdateState,
                checkForUpdates,
                openReleasePage,
                localizer,
                GetProductVersion());
            settingsWindow.Closed += OnSettingsWindowClosed;
        }

        settingsWindow.ApplySettings(productSettings);
        settingsWindow.ApplyUpdateState(readUpdateState());
        settingsWindow.ReportDiagnostic(settingsDiagnostic);
        settingsWindow.Show();
        if (settingsWindow.WindowState == WindowState.Minimized)
        {
            settingsWindow.WindowState = WindowState.Normal;
        }

        _ = settingsWindow.Activate();
    }

    internal Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await InitializeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!lifetimePolicy.ShouldHideMainWindowOnClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        audio.StateChanged -= OnAudioStateChanged;
        if (bluetoothCatalog is not null)
        {
            bluetoothCatalog.StateChanged -= OnBluetoothCatalogStateChanged;
        }

        if (bluetoothOperations is not null)
        {
            bluetoothOperations.StateChanged -= OnBluetoothOperationStateChanged;
        }

        if (settingsWindow is not null)
        {
            settingsWindow.Closed -= OnSettingsWindowClosed;
            settingsWindow.Close();
            settingsWindow = null;
        }
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Closed -= OnSettingsWindowClosed;
            settingsWindow = null;
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (applyingAudioState || !VolumeSlider.IsEnabled)
        {
            return;
        }

        int value = (int)Math.Round(eventArgs.NewValue, MidpointRounding.AwayFromZero);
        VolumePercentText.Text = $"{value}%";
        audio.PreviewVolume(value);
    }

    private async void OnVolumeCommit(object sender, MouseButtonEventArgs eventArgs)
    {
        int value = (int)Math.Round(VolumeSlider.Value, MidpointRounding.AwayFromZero);
        await RunAudioOperationAsync(() => audio.CommitVolumeAsync(value).AsTask());
    }

    private async void OnVolumeKeyUp(object sender, WpfKeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or
            Key.Home or Key.End or Key.PageUp or Key.PageDown))
        {
            return;
        }

        int value = (int)Math.Round(VolumeSlider.Value, MidpointRounding.AwayFromZero);
        await RunAudioOperationAsync(() => audio.CommitVolumeAsync(value).AsTask());
    }

    private async void OnVolumeMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (!VolumeSlider.IsEnabled || eventArgs.Delta == 0)
        {
            return;
        }

        int notches = Math.Max(1, Math.Abs(eventArgs.Delta) / Mouse.MouseWheelDeltaForOneLine);
        int direction = Math.Sign(eventArgs.Delta);
        int current = (int)Math.Round(VolumeSlider.Value, MidpointRounding.AwayFromZero);
        int value = Math.Clamp(
            current + (direction * notches * MouseWheelStepPercent),
            (int)VolumeSlider.Minimum,
            (int)VolumeSlider.Maximum);

        eventArgs.Handled = true;
        VolumeSlider.Value = value;
        await RunAudioOperationAsync(() => audio.CommitVolumeAsync(value).AsTask());
    }

    private async void OnToggleMute(object sender, RoutedEventArgs eventArgs)
    {
        await RunAudioOperationAsync(() => audio.ToggleMuteAsync().AsTask());
    }

    private async void OnRefresh(object sender, RoutedEventArgs eventArgs)
    {
        await RefreshAllAsync();
    }

    internal Task RequestRefreshAsync() => RefreshAllAsync();

    internal Task RequestBluetoothRefreshAsync() => RefreshBluetoothAsync();

    internal async Task RecoverAfterSystemChangeAsync()
    {
        if (IsVisible)
        {
            PositionAboveTaskbarCore(placementAnchor);
        }

        await RefreshAllAsync(suppressBluetoothCancellation: true);
    }

    internal void ClearPlacementAnchor() => placementAnchor = null;

    internal Task SetTaskbarSurfaceVisibleAsync(bool visible) => PersistProductSettingsAsync(
        productSettings with
        {
            DisplayMode = visible ? QuickPodsDisplayMode.Auto : QuickPodsDisplayMode.TrayOnly,
        });

    internal void PositionAbovePrimaryTaskbar()
    {
        placementAnchor = null;
        PositionAboveTaskbarCore(null);
    }

    internal void PositionAboveTaskbar(TaskbarSurfaceAnchor? anchor)
    {
        placementAnchor = anchor;
        PositionAboveTaskbarCore(anchor);
    }

    internal bool IsPointerWithinFlyoutBounds()
    {
        nint windowHandle = new WindowInteropHelper(this).Handle;
        return FlyoutPointerProbe.IsPointerWithin(windowHandle);
    }

    private void PositionAboveTaskbarCore(TaskbarSurfaceAnchor? anchor)
    {
        UpdateLayout();
        Rect workArea = SystemParameters.WorkArea;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : MinHeight;
        double preferredLeft = workArea.Left + ((workArea.Width - width) / 2);
        double preferredTop = workArea.Bottom - height - 8;
        if (anchor is not null)
        {
            preferredLeft = anchor.CenterXDip - (width / 2d);
            preferredTop = anchor.TopDip - height - 2d;
        }

        Left = Math.Clamp(
            preferredLeft,
            workArea.Left + 8,
            Math.Max(workArea.Left + 8, workArea.Right - width - 8));
        double bottomInset = anchor is null ? 8d : 0d;
        Top = Math.Clamp(
            preferredTop,
            workArea.Top + 8,
            Math.Max(workArea.Top + 8, workArea.Bottom - height - bottomInset));
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs) =>
        ApplyNativeWindowTheme();

    private void OnContentRendered(object? sender, EventArgs eventArgs)
    {
        ApplyNativeWindowTheme();
        PositionAboveTaskbarCore(placementAnchor);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (!eventArgs.HeightChanged || !IsVisible || placementRefreshPending)
        {
            return;
        }

        placementRefreshPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                placementRefreshPending = false;
                if (IsVisible)
                {
                    PositionAboveTaskbarCore(placementAnchor);
                }
            });
    }

    private void OnWindowPreviewKeyDown(object sender, WpfKeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            eventArgs.Handled = true;
            Close();
        }
    }

    private void OnFlyoutMouseEnter(object sender, System.Windows.Input.MouseEventArgs eventArgs) =>
        FlyoutPointerEntered?.Invoke(this, EventArgs.Empty);

    private void OnFlyoutMouseLeave(object sender, System.Windows.Input.MouseEventArgs eventArgs) =>
        FlyoutPointerExited?.Invoke(this, EventArgs.Empty);

    private async void OnBluetoothSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (applyingBluetoothState ||
            bluetoothCatalog is null ||
            BluetoothDeviceList.SelectedItem is not BluetoothDeviceRowPresentation selected ||
            selected.DeviceKey == bluetoothCatalog.State.SelectedDeviceKey)
        {
            return;
        }

        try
        {
            bluetoothCatalogError = null;
            await bluetoothCatalog.SelectAsync(selected.DeviceKey);
        }
        catch (Exception exception)
        {
            ReportBluetoothCatalogFailure(exception.Message);
        }
    }

    private async void OnPrimaryAction(object sender, RoutedEventArgs eventArgs)
    {
        if (!bluetoothView.IsPrimaryActionEnabled)
        {
            return;
        }

        switch (bluetoothView.PrimaryAction)
        {
            case ProductPrimaryActionKind.Connect:
                await RunBluetoothMutationAsync(
                    () => bluetoothOperations!.ConnectSelectedAsync(
                        productSettings.SetConnectedDeviceAsDefault).AsTask());
                break;
            case ProductPrimaryActionKind.MakeDefault:
                await RunBluetoothMutationAsync(
                    () => bluetoothOperations!.ConnectSelectedAsync(
                        setConnectedDeviceAsDefault: true).AsTask());
                break;
            case ProductPrimaryActionKind.Disconnect:
                if (productSettings.ConfirmBluetoothDisconnect &&
                    WpfMessageBox.Show(
                        this,
                        localizer["DisconnectConfirmMessage"],
                        localizer["DisconnectConfirmTitle"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    return;
                }

                await RunBluetoothMutationAsync(
                    () => bluetoothOperations!.DisconnectSelectedAsync().AsTask());
                break;
            case ProductPrimaryActionKind.OpenBluetoothSettings:
                OnOpenBluetoothSettings(sender, eventArgs);
                break;
        }
    }

    private void OnOpenSoundSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (!settingsLauncher.TryOpenSoundSettings())
        {
            BluetoothDiagnosticText.Text = localizer["SoundSettingsOpenFailed"];
        }
    }

    private void OnOpenQuickPodsSettings(object sender, RoutedEventArgs eventArgs) =>
        ShowSettingsWindow();

    private void OnOpenBluetoothSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (!settingsLauncher.TryOpenBluetoothSettings())
        {
            BluetoothDiagnosticText.Text = localizer["BluetoothSettingsOpenFailed"];
        }
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyAudioState(eventArgs.State));
    }

    private void OnBluetoothCatalogStateChanged(
        object? sender,
        BluetoothAudioCatalogSnapshot catalog)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            bluetoothRefreshing = false;
            bluetoothCatalogError = null;
            ApplyBluetoothPresentation();
        });
    }

    private void OnBluetoothOperationStateChanged(
        object? sender,
        BluetoothOperationSnapshot operation) =>
        _ = Dispatcher.InvokeAsync(ApplyBluetoothPresentation);

    private void ApplyAudioState(AudioState state)
    {
        applyingAudioState = true;
        try
        {
            bool available = state.Capability == AudioCapability.Available;
            EndpointNameText.Text = state.EndpointDisplayName ?? localizer["NoOutputDevice"];
            VolumeSlider.IsEnabled = available;
            MuteButton.IsEnabled = available;
            VolumeSlider.Value = state.VolumePercent;
            VolumePercentText.Text = available ? $"{state.VolumePercent}%" : "--%";
            MuteButton.ToolTip = state.IsMuted ? localizer["Unmute"] : localizer["Mute"];
            System.Windows.Automation.AutomationProperties.SetName(
                MuteButton,
                state.IsMuted ? localizer["UnmuteName"] : localizer["MuteName"]);
            MuteGlyph.Text = FluentAudioGlyphs.ForMuteState(state.IsMuted);
            AudioStatusText.Text = state.Capability switch
            {
                AudioCapability.Available => state.IsMuted
                    ? localizer["Muted"]
                    : localizer["Available"],
                AudioCapability.ServiceUnavailable => localizer["AudioServiceUnavailable"],
                _ => localizer["NoOutputDeviceShort"],
            };
        }
        finally
        {
            applyingAudioState = false;
        }
    }

    private void ApplyBluetoothPresentation()
    {
        bluetoothView = BluetoothProductPresenter.Project(
            bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty,
            bluetoothOperations?.State ?? BluetoothOperationSnapshot.Idle,
            bluetoothRefreshing,
            bluetoothCatalogError,
            localizer);
        applyingBluetoothState = true;
        try
        {
            BluetoothDeviceList.ItemsSource = bluetoothView.Devices;
            BluetoothDeviceList.SelectedValue = bluetoothView.SelectedDeviceKey;
            BluetoothDeviceList.IsHitTestVisible = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
            BluetoothDeviceList.Visibility = bluetoothView.HasDevices
                ? Visibility.Visible
                : Visibility.Collapsed;
            BluetoothSectionTitle.Text = bluetoothView.HasDevices
                ? localizer["SelectBluetoothAudio"]
                : localizer["BluetoothAudio"];
            BluetoothEmptyState.Visibility = bluetoothView.HasDevices
                ? Visibility.Collapsed
                : Visibility.Visible;
            BluetoothEmptyText.Text = bluetoothView.IsRefreshing
                ? localizer["SearchingDevices"]
                : localizer["DevicesNotFound"];
            BluetoothEmptyDescriptionText.Text = bluetoothView.IsRefreshing
                ? localizer["CheckingPairedBluetooth"]
                : localizer["NoPairedBluetoothAudio"];
            EmptyRefreshButton.IsEnabled = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
            PrimaryActionText.Text = bluetoothView.PrimaryActionText;
            PrimaryBusyIndicator.Visibility = bluetoothView.IsRefreshing || bluetoothView.IsBusy
                ? Visibility.Visible
                : Visibility.Collapsed;
            System.Windows.Automation.AutomationProperties.SetName(
                PrimaryActionButton,
                bluetoothView.PrimaryActionText);
            System.Windows.Automation.AutomationProperties.SetHelpText(
                BluetoothDeviceList,
                bluetoothView.HasDevices
                    ? localizer["DeviceListHelpDetailed"]
                    : BluetoothEmptyText.Text);
            PrimaryActionButton.IsEnabled = bluetoothView.IsPrimaryActionEnabled;
            BluetoothDiagnosticText.Text = bluetoothView.ErrorMessage ?? string.Empty;
            RefreshButton.IsEnabled = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
        }
        finally
        {
            applyingBluetoothState = false;
        }
    }

    private async Task RefreshBluetoothAsync(bool suppressCancellation = false)
    {
        if (bluetoothCatalog is null || bluetoothView.IsBusy || bluetoothRefreshing)
        {
            return;
        }

        bluetoothRefreshing = true;
        bluetoothCatalogError = null;
        ApplyBluetoothPresentation();
        try
        {
            await bluetoothCatalog.RefreshAsync();
        }
        catch (OperationCanceledException) when (suppressCancellation)
        {
            bluetoothCatalogError = null;
        }
        catch (Exception exception)
        {
            ReportBluetoothCatalogFailure(exception.Message);
        }
        finally
        {
            bluetoothRefreshing = false;
            ApplyBluetoothPresentation();
        }
    }

    private async Task RefreshAllAsync(bool suppressBluetoothCancellation = false)
    {
        await RunAudioOperationAsync(() => audio.InitializeAsync().AsTask());
        await RefreshBluetoothAsync(suppressBluetoothCancellation);
    }

    private async Task InitializeSettingsAsync()
    {
        QuickPodsSettings stored;
        try
        {
            stored = await settingsStore.LoadSettingsAsync();
            if (settingsStore.LastRecovery is { } recovery)
            {
                ReportSettingsFailure(
                    localizer.Format(
                        "CorruptSettingsRecovered",
                        Path.GetFileName(recovery.QuarantinedPath)));
            }
        }
        catch (Exception exception)
        {
            ReportSettingsFailure(localizer.Format("SettingsLoadFailed", exception.Message));
            ApplyProductSettings(QuickPodsSettings.Default);
            return;
        }

        try
        {
            await startupRegistration.SetEnabledAsync(stored.StartWithWindows);
            bool registered = await startupRegistration.IsEnabledAsync();
            if (stored.StartWithWindows != registered)
            {
                throw new InvalidOperationException(localizer["StartupVerifyFailed"]);
            }

        }
        catch (Exception exception)
        {
            ReportSettingsFailure(
                localizer.Format("StartupVerifyFailedWithDetail", exception.Message));
            bool? actual = await TryReadStartupRegistrationAsync();
            if (actual is not null)
            {
                stored = stored with { StartWithWindows = actual.Value };
            }
        }

        ApplyProductSettings(stored);
    }

    private async Task PersistProductSettingsAsync(QuickPodsSettings requested)
    {
        try
        {
            _ = await UpdateProductSettingsAsync(requested);
        }
        catch (Exception exception)
        {
            ReportSettingsFailure(localizer.Format("SettingsSaveFailed", exception.Message));
            ApplyProductSettings(productSettings);
        }
    }

    private async Task<QuickPodsSettings> UpdateProductSettingsAsync(
        QuickPodsSettings requested)
    {
        QuickPodsSettings saved = await settingsStore.UpdateSettingsAsync(current => current with
        {
            DisplayMode = requested.DisplayMode,
            TaskbarPlacement = requested.TaskbarPlacement,
            Theme = requested.Theme,
            Language = requested.Language,
            MouseWheelStepPercent = requested.MouseWheelStepPercent,
            SetConnectedDeviceAsDefault = requested.SetConnectedDeviceAsDefault,
            ConfirmBluetoothDisconnect = requested.ConfirmBluetoothDisconnect,
            CheckForUpdatesAtStartup = requested.CheckForUpdatesAtStartup,
        });
        ClearSettingsDiagnostic();
        ApplyProductSettings(saved);
        return saved;
    }

    internal async Task RecordUpdateCheckMetadataAsync(
        ApplicationUpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        QuickPodsSettings saved = await settingsStore.UpdateSettingsAsync(current => current with
        {
            LastUpdateCheckUtc = result.CheckedAtUtc,
            LastKnownLatestVersion = result.LatestVersion.ToString(3),
            LastKnownReleasePage = result.ReleasePage.AbsoluteUri,
        });
        ApplyProductSettings(saved);
    }

    private async Task<QuickPodsSettings> UpdateStartupSettingAsync(bool requested)
    {
        bool previous = productSettings.StartWithWindows;
        try
        {
            await startupRegistration.SetEnabledAsync(requested);
            bool actual = await startupRegistration.IsEnabledAsync();
            if (actual != requested)
            {
                throw new InvalidOperationException(localizer["StartupVerifyFailed"]);
            }

            QuickPodsSettings saved = await settingsStore.UpdateSettingsAsync(
                current => current with { StartWithWindows = actual });
            ClearSettingsDiagnostic();
            ApplyProductSettings(saved);
            return saved;
        }
        catch
        {
            try
            {
                await startupRegistration.SetEnabledAsync(previous);
            }
            catch
            {
            }

            bool? actual = await TryReadStartupRegistrationAsync();
            ApplyProductSettings(productSettings with
            {
                StartWithWindows = actual ?? previous,
            });
            throw;
        }
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(logsDirectory);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = logsDirectory,
            UseShellExecute = true,
        });
    }

    private void ClearSettingsDiagnostic()
    {
        settingsDiagnostic = string.Empty;
        SettingsDiagnosticText.Text = string.Empty;
        settingsWindow?.ReportDiagnostic(string.Empty);
    }

    private async Task InitializeCoreAsync()
    {
        await RunAudioOperationAsync(() => audio.InitializeAsync().AsTask());
        await InitializeSettingsAsync();
    }

    private async Task<bool?> TryReadStartupRegistrationAsync()
    {
        try
        {
            return await startupRegistration.IsEnabledAsync();
        }
        catch
        {
            return null;
        }
    }

    private int MouseWheelStepPercent => productSettings.MouseWheelStepPercent;

    private void ApplyProductSettings(QuickPodsSettings settings)
    {
        productSettings = settings.Normalize();
        settingsWindow?.ApplySettings(productSettings);
        SettingsChanged?.Invoke(productSettings);
        ApplyNativeWindowTheme();
    }

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

        bool useDarkFrame = System.Windows.Application.Current.Resources["WindowBrush"] is
            SolidColorBrush brush &&
            ((brush.Color.R * 299) + (brush.Color.G * 587) + (brush.Color.B * 114)) < 128000;
        int darkMode = useDarkFrame ? 1 : 0;
        int cornerPreference = DwmWindowCornerRound;
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
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private string CreateDiagnosticSummary()
    {
        BluetoothDeviceRowPresentation? selected = bluetoothView.Devices.FirstOrDefault(
            device => device.IsSelected);
        var builder = new StringBuilder();
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"QuickPods {GetProductVersion()}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Audio capability: {audio.State.Capability}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Audio volume: {audio.State.VolumePercent}%");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Audio muted: {audio.State.IsMuted}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Bluetooth device count: {bluetoothView.Devices.Length}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Selected device: {selected?.DisplayName ?? "None"}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Selected status: {selected?.StatusText ?? "None"}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Display mode: {productSettings.DisplayMode}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Taskbar placement: {productSettings.TaskbarPlacement}");
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"Theme: {productSettings.Theme}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Language: {productSettings.Language}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Wheel step: {productSettings.MouseWheelStepPercent}%");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Start with Windows: {productSettings.StartWithWindows}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Automatic update checks: {productSettings.CheckForUpdatesAtStartup}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Last update check: {productSettings.LastUpdateCheckUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "Never"}");
        return builder.ToString();
    }

    private static string GetProductVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private async Task RunBluetoothMutationAsync(
        Func<Task<BluetoothOperationSnapshot>> operation)
    {
        if (bluetoothOperations is null)
        {
            return;
        }

        try
        {
            bluetoothCatalogError = null;
            _ = await operation();
            await RefreshBluetoothAsync();
            ApplyBluetoothPresentation();
        }
        catch (Exception exception)
        {
            bluetoothCatalogError = exception.Message;
            ApplyBluetoothPresentation();
        }
    }

    private async Task RunAudioOperationAsync(Func<Task<AudioState>> operation)
    {
        try
        {
            AudioState state = await operation();
            ApplyAudioState(state);
            AudioDiagnosticText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            AudioDiagnosticText.Text = exception.Message;
            ApplyAudioState(AudioState.Unavailable);
        }
    }

    private async Task RunAudioOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            ApplyAudioState(audio.State);
            AudioDiagnosticText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            AudioDiagnosticText.Text = exception.Message;
            ApplyAudioState(AudioState.Unavailable);
        }
    }
}
