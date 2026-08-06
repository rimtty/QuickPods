using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuickPods.Contracts;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Settings;
using QuickPods.Presentation;
using QuickPods.Windows.Settings;
using WpfClipboard = System.Windows.Clipboard;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;

namespace QuickPods.App;

public partial class MainWindow : Window
{
    private readonly AudioController audio;
    private readonly BluetoothCatalogController? bluetoothCatalog;
    private readonly BluetoothOperationController? bluetoothOperations;
    private readonly IWindowsSettingsLauncher settingsLauncher;
    private readonly JsonBluetoothSelectionStore settingsStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly ProductLifetimePolicy lifetimePolicy;
    private readonly string logsDirectory;
    private bool applyingAudioState;
    private bool applyingBluetoothState;
    private bool applyingSettings;
    private bool bluetoothRefreshing;
    private string? bluetoothCatalogError;
    private BluetoothProductPresentation bluetoothView;
    private QuickPodsSettings productSettings = QuickPodsSettings.Default;
    private TaskbarSurfaceAnchor? placementAnchor;
    private bool placementRefreshPending;
    private Task? initialization;

    internal event Action<QuickPodsSettings>? SettingsChanged;

    internal event EventHandler? FlyoutPointerEntered;

    internal event EventHandler? FlyoutPointerExited;

    public MainWindow(
        AudioController audio,
        BluetoothCatalogController? bluetoothCatalog,
        BluetoothOperationController? bluetoothOperations,
        IWindowsSettingsLauncher settingsLauncher,
        JsonBluetoothSelectionStore settingsStore,
        IStartupRegistration startupRegistration,
        ProductLifetimePolicy lifetimePolicy,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        this.logsDirectory = Path.GetFullPath(logsDirectory);
        bluetoothRefreshing = bluetoothCatalog is not null;
        bluetoothCatalogError = bluetoothCatalog is null
            ? "Bluetoothサービスを初期化できませんでした。"
            : null;
        bluetoothView = BluetoothProductPresenter.Project(
            bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty,
            bluetoothOperations?.State ?? BluetoothOperationSnapshot.Idle,
            bluetoothRefreshing,
            bluetoothCatalogError);

        InitializeComponent();
        InitializeSettingsChoices();
        VersionText.Text = $"QuickPods {GetProductVersion()}";
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
            ? "Bluetoothデバイス一覧を更新できませんでした。"
            : $"Bluetoothデバイス一覧を更新できませんでした: {message}";
        ApplyBluetoothPresentation();
    }

    internal void ReportSettingsFailure(string message)
    {
        SettingsDiagnosticText.Text = message;
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

    internal async Task RecoverAfterSystemChangeAsync()
    {
        if (IsVisible)
        {
            PositionAboveTaskbarCore(placementAnchor);
        }

        await RefreshAllAsync();
    }

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
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double anchorCenter = ((anchor.Left + anchor.Right) / 2d) / dpi.DpiScaleX;
            preferredLeft = anchorCenter - (width / 2d);
            preferredTop = (anchor.Top / dpi.DpiScaleY) - height - 2d;
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

    private void OnContentRendered(object? sender, EventArgs eventArgs) =>
        PositionAboveTaskbarCore(placementAnchor);

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
                        "選択したBluetoothオーディオを切断しますか？",
                        "QuickPods",
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
            BluetoothDiagnosticText.Text = "Windowsのサウンド設定を開けませんでした。";
        }
    }

    private void OnOpenBluetoothSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (!settingsLauncher.TryOpenBluetoothSettings())
        {
            BluetoothDiagnosticText.Text = "WindowsのBluetooth設定を開けませんでした。";
        }
    }

    private async void OnStartWithWindowsChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings)
        {
            return;
        }

        bool requested = StartWithWindowsCheckBox.IsChecked == true;
        bool previous = productSettings.StartWithWindows;
        StartWithWindowsCheckBox.IsEnabled = false;
        SettingsDiagnosticText.Text = string.Empty;
        try
        {
            await startupRegistration.SetEnabledAsync(requested);
            bool actual = await startupRegistration.IsEnabledAsync();
            if (actual != requested)
            {
                throw new InvalidOperationException("自動起動設定を確認できませんでした。");
            }

            QuickPodsSettings saved = await settingsStore.UpdateSettingsAsync(
                current => current with { StartWithWindows = actual });
            ApplyProductSettings(saved);
        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"自動起動設定を変更できませんでした: {exception.Message}";
            try
            {
                await startupRegistration.SetEnabledAsync(previous);
            }
            catch
            {
            }

            bool? actual = await TryReadStartupRegistrationAsync();
            ApplyStartWithWindows(actual ?? previous);
        }
        finally
        {
            StartWithWindowsCheckBox.IsEnabled = true;
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = logsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"ログフォルダーを開けませんでした: {exception.Message}";
        }
    }

    private void OnCopyDiagnostics(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            WpfClipboard.SetText(CreateDiagnosticSummary());
            SettingsDiagnosticText.Text = "診断情報をクリップボードへコピーしました。";
        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"診断情報をコピーできませんでした: {exception.Message}";
        }
    }

    private async void OnProductSettingChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (applyingSettings ||
            DisplayModeComboBox.SelectedValue is not QuickPodsDisplayMode displayMode ||
            ThemeComboBox.SelectedValue is not QuickPodsThemeMode theme ||
            MouseWheelStepComboBox.SelectedValue is not int wheelStep)
        {
            return;
        }

        QuickPodsSettings requested = productSettings with
        {
            DisplayMode = displayMode,
            Theme = theme,
            MouseWheelStepPercent = wheelStep,
            SetConnectedDeviceAsDefault = SetConnectedDeviceAsDefaultCheckBox.IsChecked == true,
            ConfirmBluetoothDisconnect = ConfirmBluetoothDisconnectCheckBox.IsChecked == true,
        };
        await PersistProductSettingsAsync(requested);
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
            EndpointNameText.Text = state.EndpointDisplayName ?? "利用可能な出力デバイスがありません";
            VolumeSlider.IsEnabled = available;
            MuteButton.IsEnabled = available;
            VolumeSlider.Value = state.VolumePercent;
            VolumePercentText.Text = available ? $"{state.VolumePercent}%" : "--%";
            MuteButton.ToolTip = state.IsMuted ? "ミュート解除" : "ミュート";
            System.Windows.Automation.AutomationProperties.SetName(
                MuteButton,
                state.IsMuted ? "ミュートを解除" : "ミュートにする");
            MuteGlyph.Text = state.IsMuted ? "\uE74F" : "\uE767";
            AudioStatusText.Text = state.Capability switch
            {
                AudioCapability.Available => state.IsMuted ? "ミュート中" : "利用可能",
                AudioCapability.ServiceUnavailable => "Audio service利用不可",
                _ => "出力デバイスなし",
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
            bluetoothCatalogError);
        applyingBluetoothState = true;
        try
        {
            BluetoothDeviceList.ItemsSource = bluetoothView.Devices;
            BluetoothDeviceList.SelectedValue = bluetoothView.SelectedDeviceKey;
            BluetoothDeviceList.IsEnabled = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
            BluetoothDeviceList.Visibility = bluetoothView.HasDevices
                ? Visibility.Visible
                : Visibility.Collapsed;
            BluetoothSectionTitle.Text = bluetoothView.HasDevices
                ? "Bluetoothオーディオを選択"
                : "Bluetoothオーディオ";
            BluetoothEmptyState.Visibility = bluetoothView.HasDevices
                ? Visibility.Collapsed
                : Visibility.Visible;
            BluetoothEmptyText.Text = bluetoothView.IsRefreshing
                ? "デバイスを検索しています…"
                : "デバイスが見つかりません";
            BluetoothEmptyDescriptionText.Text = bluetoothView.IsRefreshing
                ? "ペアリング済みのBluetoothオーディオを確認しています"
                : "ペアリング済みのBluetoothオーディオがありません";
            EmptyRefreshButton.IsEnabled = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
            PrimaryActionButton.Content = bluetoothView.PrimaryActionText;
            System.Windows.Automation.AutomationProperties.SetName(
                PrimaryActionButton,
                bluetoothView.PrimaryActionText);
            System.Windows.Automation.AutomationProperties.SetHelpText(
                BluetoothDeviceList,
                bluetoothView.HasDevices
                    ? "上下矢印でデバイスを選択します。選択だけでは接続状態を変更しません。"
                    : BluetoothEmptyText.Text);
            PrimaryActionButton.IsEnabled = bluetoothView.IsPrimaryActionEnabled;
            SoundSettingsButton.Visibility = bluetoothView.HasDevices
                ? Visibility.Visible
                : Visibility.Collapsed;
            BluetoothSettingsButton.Visibility = bluetoothView.HasDevices
                ? Visibility.Collapsed
                : Visibility.Visible;
            BluetoothDiagnosticText.Text = bluetoothView.ErrorMessage ?? string.Empty;
            RefreshButton.IsEnabled = !bluetoothView.IsRefreshing && !bluetoothView.IsBusy;
        }
        finally
        {
            applyingBluetoothState = false;
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        if (bluetoothCatalog is null || bluetoothView.IsBusy)
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

    private async Task RefreshAllAsync()
    {
        await RunAudioOperationAsync(() => audio.InitializeAsync().AsTask());
        await RefreshBluetoothAsync();
    }

    private async Task InitializeSettingsAsync()
    {
        QuickPodsSettings stored;
        try
        {
            stored = await settingsStore.LoadSettingsAsync();
            if (settingsStore.LastRecovery is { } recovery)
            {
                SettingsDiagnosticText.Text =
                    $"破損した設定を {Path.GetFileName(recovery.QuarantinedPath)} へ隔離し、安全な初期値で起動しました。";
            }
        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"設定を読み込めませんでした: {exception.Message}";
            ApplyProductSettings(QuickPodsSettings.Default);
            return;
        }

        try
        {
            await startupRegistration.SetEnabledAsync(stored.StartWithWindows);
            bool registered = await startupRegistration.IsEnabledAsync();
            if (stored.StartWithWindows != registered)
            {
                throw new InvalidOperationException("自動起動設定を確認できませんでした。");
            }

        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"自動起動設定を確認できませんでした: {exception.Message}";
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
        SettingsDiagnosticText.Text = string.Empty;
        try
        {
            QuickPodsSettings saved = await settingsStore.UpdateSettingsAsync(current => current with
            {
                DisplayMode = requested.DisplayMode,
                Theme = requested.Theme,
                MouseWheelStepPercent = requested.MouseWheelStepPercent,
                SetConnectedDeviceAsDefault = requested.SetConnectedDeviceAsDefault,
                ConfirmBluetoothDisconnect = requested.ConfirmBluetoothDisconnect,
            });
            ApplyProductSettings(saved);
        }
        catch (Exception exception)
        {
            SettingsDiagnosticText.Text = $"設定を保存できませんでした: {exception.Message}";
            ApplyProductSettings(productSettings);
        }
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

    private void ApplyStartWithWindows(bool enabled)
    {
        ApplyProductSettings(productSettings with { StartWithWindows = enabled });
    }

    private int MouseWheelStepPercent => productSettings.MouseWheelStepPercent;

    private void InitializeSettingsChoices()
    {
        applyingSettings = true;
        try
        {
            DisplayModeComboBox.ItemsSource = new SettingChoice<QuickPodsDisplayMode>[]
            {
                new(QuickPodsDisplayMode.Auto, "自動"),
                new(QuickPodsDisplayMode.TrayOnly, "通知領域のみ"),
            };
            ThemeComboBox.ItemsSource = new SettingChoice<QuickPodsThemeMode>[]
            {
                new(QuickPodsThemeMode.System, "Windowsに合わせる"),
                new(QuickPodsThemeMode.Dark, "ダーク"),
                new(QuickPodsThemeMode.Light, "ライト"),
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

    private void ApplyProductSettings(QuickPodsSettings settings)
    {
        productSettings = settings.Normalize();
        applyingSettings = true;
        try
        {
            DisplayModeComboBox.SelectedValue = productSettings.DisplayMode;
            ThemeComboBox.SelectedValue = productSettings.Theme;
            MouseWheelStepComboBox.SelectedValue = productSettings.MouseWheelStepPercent;
            SetConnectedDeviceAsDefaultCheckBox.IsChecked =
                productSettings.SetConnectedDeviceAsDefault;
            ConfirmBluetoothDisconnectCheckBox.IsChecked =
                productSettings.ConfirmBluetoothDisconnect;
            StartWithWindowsCheckBox.IsChecked = productSettings.StartWithWindows;
        }
        finally
        {
            applyingSettings = false;
        }

        SettingsChanged?.Invoke(productSettings);
    }

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
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"Theme: {productSettings.Theme}");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Wheel step: {productSettings.MouseWheelStepPercent}%");
        _ = builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Start with Windows: {productSettings.StartWithWindows}");
        return builder.ToString();
    }

    private static string GetProductVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private sealed record SettingChoice<T>(T Value, string Text);

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
            BluetoothOperationSnapshot result = await operation();
            string? operationError = BluetoothProductPresenter.Project(
                bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty,
                result,
                isRefreshing: false).ErrorMessage;
            await RefreshBluetoothAsync();
            if (operationError is not null)
            {
                bluetoothCatalogError = operationError;
            }

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
