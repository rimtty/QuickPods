using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickPods.Contracts;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Logging;
using QuickPods.Infrastructure.Runtime;
using QuickPods.Infrastructure.Settings;
using QuickPods.Presentation;
using QuickPods.Windows.Audio;
using QuickPods.Windows.Bluetooth;
using QuickPods.Windows.Settings;
using QuickPods.Windows.Startup;
using MediaColor = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;
using WpfSystemColors = System.Windows.SystemColors;

namespace QuickPods.App;

public partial class App : WpfApplication, IDisposable
{
    private const string SingleInstanceId = "QuickPods.App.v1";

    private AudioController? controller;
    private BluetoothCatalogController? bluetoothCatalog;
    private WindowsBluetoothAudioCatalogPort? bluetoothCatalogPort;
    private BluetoothOperationController? bluetoothOperations;
    private WindowsBluetoothDeviceOperationPort? bluetoothOperationPort;
    private WindowsDefaultOutputOperationPort? defaultOutputOperationPort;
    private JsonBluetoothSelectionStore? bluetoothSelectionStore;
    private CancellationTokenSource? bluetoothLifetime;
    private Task? bluetoothInitialization;
    private TaskbarHostProcessManager? taskbarHost;
    private SingleInstanceLease? singleInstance;
    private TrayIconController? trayIcon;
    private WindowsCoreAudioEndpointPort? windowsAudio;
    private JsonLineLogger? logger;
    private string logsDirectory = string.Empty;
    private readonly ProductLifetimePolicy lifetimePolicy = new();
    private readonly HashSet<string> pendingLifecycleReasons = new(StringComparer.Ordinal);
    private QuickPodsSettings productSettings = QuickPodsSettings.Default;
    private DispatcherTimer? lifecycleRecoveryTimer;
    private bool lifecycleRecoveryRunning;
    private bool lifecycleEventsSubscribed;
    private bool productSettingsInitialized;
    private bool disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 1 && string.Equals(
            e.Args[0],
            "--unregister-startup",
            StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(RunUnregisterStartupCommand());
            return;
        }

        singleInstance = SingleInstanceLease.TryAcquire(SingleInstanceId);
        if (!singleInstance.IsPrimary)
        {
            singleInstance.SignalPrimary();
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown();
            return;
        }

        singleInstance.ActivationRequested += OnActivationRequested;
        singleInstance.StartActivationListener();
        InitializeLogging();
        Log(QuickPodsLogLevel.Information, "ApplicationStarted", "QuickPods started.");

        IAudioEndpointPort audioPort;
        string? startupDiagnostic = null;
        try
        {
            windowsAudio = new WindowsCoreAudioEndpointPort();
            audioPort = windowsAudio;
        }
        catch (Exception exception)
        {
            audioPort = new UnavailableAudioEndpointPort();
            startupDiagnostic = $"Core Audioを初期化できませんでした: {exception.Message}";
        }

        controller = new AudioController(audioPort);
        InitializeSettingsStore();
        StartBluetoothCatalog();
        var settingsLauncher = new WindowsSettingsLauncher();
        string applicationExecutable = Path.Combine(AppContext.BaseDirectory, "QuickPods.exe");
        var startupRegistration = new WindowsStartupRegistration(applicationExecutable);
        var window = new MainWindow(
            controller,
            bluetoothCatalog,
            bluetoothOperations,
            settingsLauncher,
            bluetoothSelectionStore!,
            startupRegistration,
            lifetimePolicy,
            logsDirectory,
            startupDiagnostic);
        window.SettingsChanged += OnProductSettingsChanged;
        MainWindow = window;

        bool startInBackground = e.Args.Any(argument => string.Equals(
            argument,
            "--background",
            StringComparison.OrdinalIgnoreCase));
        try
        {
            trayIcon = new TrayIconController(
                ShowMainWindow,
                () => _ = window.RequestRefreshAsync(),
                visible => _ = window.SetTaskbarSurfaceVisibleAsync(visible),
                () => settingsLauncher.TryOpenSoundSettings(),
                () => settingsLauncher.TryOpenBluetoothSettings(),
                ExitApplication);
        }
        catch (Exception exception)
        {
            startInBackground = false;
            window.ReportSettingsFailure(
                $"通知領域アイコンを初期化できませんでした: {exception.Message}");
        }

        if (startInBackground)
        {
            _ = window.InitializeAsync();
        }
        else
        {
            window.Show();
        }

        string hostExecutable = Path.Combine(AppContext.BaseDirectory, "QuickPods.TaskbarHost.exe");
        taskbarHost = new TaskbarHostProcessManager(hostExecutable);
        controller.StateChanged += OnAudioStateChanged;
        taskbarHost.InteractionReceived += OnHostInteractionReceived;
        taskbarHost.StateChanged += OnTaskbarHostStateChanged;
        taskbarHost.Start(CreateTaskbarSnapshot(controller.State));
        InitializeSystemLifecycle();

        if (bluetoothCatalog is not null && bluetoothLifetime is not null)
        {
            bluetoothInitialization = InitializeBluetoothCatalogAsync(bluetoothLifetime.Token);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        lifetimePolicy.RequestExit();
        base.OnSessionEnding(e);
    }

    private static int RunUnregisterStartupCommand()
    {
        try
        {
            string applicationExecutable = Path.Combine(AppContext.BaseDirectory, "QuickPods.exe");
            var startupRegistration = new WindowsStartupRegistration(applicationExecutable);
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickPods",
                "settings.json");
            var cleanup = new UninstallCleanupService(
                startupRegistration,
                new JsonSettingsStore<QuickPodsSettings>(settingsPath));
            cleanup.ExecuteAsync(File.Exists(settingsPath)).AsTask().GetAwaiter().GetResult();

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimePolicy.RequestExit();
        Log(QuickPodsLogLevel.Information, "ApplicationStopping", "QuickPods is stopping.");
        UnsubscribeSystemLifecycle();
        bluetoothLifetime?.Cancel();
        trayIcon?.Dispose();
        trayIcon = null;
        if (controller is not null)
        {
            controller.StateChanged -= OnAudioStateChanged;
        }

        if (MainWindow is MainWindow window)
        {
            window.SettingsChanged -= OnProductSettingsChanged;
        }

        if (taskbarHost is not null)
        {
            taskbarHost.InteractionReceived -= OnHostInteractionReceived;
            taskbarHost.StateChanged -= OnTaskbarHostStateChanged;
            taskbarHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (bluetoothCatalog is not null)
        {
            bluetoothCatalog.StateChanged -= OnBluetoothCatalogStateChanged;
        }

        if (bluetoothOperations is not null)
        {
            bluetoothOperations.StateChanged -= OnBluetoothOperationStateChanged;
        }

        try
        {
            bluetoothInitialization?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bluetoothOperations?.Dispose();
        defaultOutputOperationPort?.Dispose();
        bluetoothOperationPort?.Dispose();
        bluetoothCatalog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bluetoothSelectionStore?.Dispose();
        bluetoothCatalogPort?.Dispose();
        bluetoothLifetime?.Dispose();
        windowsAudio?.Dispose();
        logger?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        logger = null;
        if (singleInstance is not null)
        {
            singleInstance.ActivationRequested -= OnActivationRequested;
            singleInstance.Dispose();
            singleInstance = null;
        }

        GC.SuppressFinalize(this);
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs) =>
        taskbarHost?.Publish(CreateTaskbarSnapshot(eventArgs.State));

    private void OnBluetoothCatalogStateChanged(
        object? sender,
        BluetoothAudioCatalogSnapshot catalog)
    {
        Log(
            QuickPodsLogLevel.Information,
            "BluetoothCatalogUpdated",
            "Bluetooth audio catalog updated.",
            new Dictionary<string, object?>
            {
                ["DeviceCount"] = catalog.Devices.Length,
                ["InventoryGeneration"] = catalog.InventoryGeneration,
                ["Revision"] = catalog.Revision,
                ["HasSelection"] = catalog.SelectedDeviceKey is not null,
            });
        taskbarHost?.Publish(CreateTaskbarSnapshot(controller?.State ?? AudioState.Unavailable, catalog));
    }

    private void OnBluetoothOperationStateChanged(
        object? sender,
        BluetoothOperationSnapshot operation)
    {
        Log(
            operation.Error is null ? QuickPodsLogLevel.Information : QuickPodsLogLevel.Warning,
            "BluetoothOperationStateChanged",
            "Bluetooth operation state changed.",
            new Dictionary<string, object?>
            {
                ["Action"] = operation.RequestedAction.ToString(),
                ["Operation"] = operation.Operation.ToString(),
                ["Outcome"] = operation.Outcome.ToString(),
                ["ConnectionState"] = operation.ConnectionState.ToString(),
                ["DefaultOutputState"] = operation.DefaultOutputState.ToString(),
                ["Error"] = operation.Error?.ToString(),
                ["Revision"] = operation.Revision,
            });
        taskbarHost?.Publish(CreateTaskbarSnapshot(
            controller?.State ?? AudioState.Unavailable,
            operation: operation));
    }

    private void OnTaskbarHostStateChanged(
        object? sender,
        TaskbarHostSupervisorState state)
    {
        Log(
            state.Lifecycle == TaskbarHostLifecycle.DisabledForSession
                ? QuickPodsLogLevel.Warning
                : QuickPodsLogLevel.Information,
            "TaskbarHostStateChanged",
            "Taskbar host lifecycle changed.",
            new Dictionary<string, object?>
            {
                ["Lifecycle"] = state.Lifecycle.ToString(),
                ["ConsecutiveFailures"] = state.ConsecutiveFailures,
                ["HasScheduledRetry"] = state.RetryAfter is not null,
            });

        if (state.Lifecycle == TaskbarHostLifecycle.DisabledForSession)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.ReportSettingsFailure(
                        "タスクバー表示をこのセッションでは停止しました。通知領域から操作できます。");
                }
            });
        }
    }

    private void OnProductSettingsChanged(QuickPodsSettings settings)
    {
        QuickPodsSettings normalized = settings.Normalize();
        if (productSettingsInitialized && normalized == productSettings)
        {
            return;
        }

        productSettings = normalized;
        productSettingsInitialized = true;
        Log(
            QuickPodsLogLevel.Information,
            "SettingsApplied",
            "Product settings applied.",
            new Dictionary<string, object?>
            {
                ["DisplayMode"] = productSettings.DisplayMode.ToString(),
                ["Theme"] = productSettings.Theme.ToString(),
                ["MouseWheelStepPercent"] = productSettings.MouseWheelStepPercent,
                ["SetConnectedDeviceAsDefault"] =
                    productSettings.SetConnectedDeviceAsDefault,
                ["ConfirmBluetoothDisconnect"] =
                    productSettings.ConfirmBluetoothDisconnect,
                ["StartWithWindows"] = productSettings.StartWithWindows,
            });
        ApplyTheme(productSettings.Theme);
        trayIcon?.SetTaskbarSurfaceVisible(
            productSettings.DisplayMode != QuickPodsDisplayMode.TrayOnly);
        taskbarHost?.Publish(CreateTaskbarSnapshot(
            controller?.State ?? AudioState.Unavailable));
    }

    private void StartBluetoothCatalog()
    {
        try
        {
            bluetoothCatalogPort = new WindowsBluetoothAudioCatalogPort();
            bluetoothCatalog = new BluetoothCatalogController(
                bluetoothCatalogPort,
                bluetoothSelectionStore!);
            bluetoothCatalog.StateChanged += OnBluetoothCatalogStateChanged;
            bluetoothOperationPort = new WindowsBluetoothDeviceOperationPort(bluetoothCatalogPort);
            defaultOutputOperationPort = new WindowsDefaultOutputOperationPort(bluetoothCatalogPort);
            bluetoothOperations = new BluetoothOperationController(
                bluetoothCatalog,
                bluetoothOperationPort,
                defaultOutputOperationPort,
                new WindowsBluetoothOperationGate());
            bluetoothOperations.StateChanged += OnBluetoothOperationStateChanged;
            bluetoothLifetime = new CancellationTokenSource();
        }
        catch
        {
            bluetoothOperations?.Dispose();
            defaultOutputOperationPort?.Dispose();
            bluetoothOperationPort?.Dispose();
            bluetoothCatalog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            bluetoothCatalogPort?.Dispose();
            bluetoothLifetime?.Dispose();
            bluetoothCatalog = null;
            bluetoothOperations = null;
            defaultOutputOperationPort = null;
            bluetoothOperationPort = null;
            bluetoothCatalogPort = null;
            bluetoothLifetime = null;
        }
    }

    private void InitializeSettingsStore()
    {
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickPods",
            "settings.json");
        var jsonSettings = new JsonSettingsStore<QuickPodsSettings>(settingsPath);
        jsonSettings.CorruptSettingsQuarantined += OnCorruptSettingsQuarantined;
        bluetoothSelectionStore = new JsonBluetoothSelectionStore(jsonSettings);
    }

    private void OnCorruptSettingsQuarantined(
        object? sender,
        SettingsRecoveryInfo recovery) =>
        Log(
            QuickPodsLogLevel.Warning,
            "CorruptSettingsQuarantined",
            "Corrupt settings were quarantined and safe defaults were loaded.",
            new Dictionary<string, object?>
            {
                ["FailureType"] = recovery.FailureType,
                ["QuarantineFile"] = Path.GetFileName(recovery.QuarantinedPath),
            });

    private void InitializeLogging()
    {
        logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickPods",
            "logs");
        try
        {
            Directory.CreateDirectory(logsDirectory);
            string logPath = Path.Combine(
                logsDirectory,
                $"quickpods-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
            var stream = new FileStream(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            logger = new JsonLineLogger(new StreamWriter(stream, new UTF8Encoding(false)));
        }
        catch
        {
            logger = null;
        }
    }

    private void Log(
        QuickPodsLogLevel level,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (logger is null)
        {
            return;
        }

        try
        {
            logger.WriteAsync(new QuickPodsLogEntry(
                DateTimeOffset.UtcNow,
                level,
                eventName,
                message,
                properties ?? new Dictionary<string, object?>())).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Logging must never disable audio control, Bluetooth fallback, or shutdown.
        }
    }

    private async Task InitializeBluetoothCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (bluetoothCatalog is not null)
            {
                await bluetoothCatalog.InitializeAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Catalog faults do not disable master-volume control or terminate the app.
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.ReportBluetoothCatalogFailure(exception.Message);
                }
            });
        }
    }

    private void OnHostInteractionReceived(object? sender, HostInteractionEnvelope interaction) =>
        _ = Dispatcher.InvokeAsync(() => HandleHostInteractionSafelyAsync(interaction)).Task.Unwrap();

    private void OnActivationRequested(object? sender, EventArgs eventArgs) =>
        _ = Dispatcher.InvokeAsync(ShowMainWindow);

    private async Task HandleHostInteractionAsync(HostInteractionEnvelope interaction)
    {
        if (controller is null)
        {
            return;
        }

        switch (interaction.Kind)
        {
            case HostInteractionKind.SetVolumePreview when interaction.VolumePercent is { } preview:
                controller.PreviewVolume(preview);
                break;
            case HostInteractionKind.SetVolumeCommit when interaction.VolumePercent is { } committed:
                await controller.CommitVolumeAsync(committed);
                break;
            case HostInteractionKind.ToggleMute:
                await controller.ToggleMuteAsync();
                break;
            case HostInteractionKind.OpenAudioFlyout:
            case HostInteractionKind.OpenContextMenu:
                ShowMainWindow();
                break;
        }
    }

    private async Task HandleHostInteractionSafelyAsync(HostInteractionEnvelope interaction)
    {
        try
        {
            await HandleHostInteractionAsync(interaction);
        }
        catch (Exception exception)
        {
            Log(
                QuickPodsLogLevel.Warning,
                "TaskbarInteractionFailed",
                "A taskbar interaction failed without terminating QuickPods.",
                new Dictionary<string, object?>
                {
                    ["InteractionKind"] = interaction.Kind.ToString(),
                    ["FailureType"] = exception.GetType().Name,
                });
            if (MainWindow is MainWindow window)
            {
                window.ReportSettingsFailure(
                    "タスクバーからの操作に失敗しました。通知領域または製品画面から再試行してください。");
            }
        }
    }

    private void InitializeSystemLifecycle()
    {
        lifecycleRecoveryTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(750),
            DispatcherPriority.Background,
            OnLifecycleRecoveryTick,
            Dispatcher)
        {
            IsEnabled = false,
        };

        try
        {
            lifecycleEventsSubscribed = true;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        }
        catch (Exception exception)
        {
            Log(
                QuickPodsLogLevel.Warning,
                "LifecycleMonitorUnavailable",
                "Windows lifecycle notifications could not be registered.",
                new Dictionary<string, object?>
                {
                    ["FailureType"] = exception.GetType().Name,
                });
            UnsubscribeSystemLifecycle();
        }
    }

    private void UnsubscribeSystemLifecycle()
    {
        lifecycleRecoveryTimer?.Stop();
        if (lifecycleRecoveryTimer is not null)
        {
            lifecycleRecoveryTimer.Tick -= OnLifecycleRecoveryTick;
            lifecycleRecoveryTimer = null;
        }

        if (!lifecycleEventsSubscribed)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        lifecycleEventsSubscribed = false;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        QueueLifecycleRecovery("DisplaySettingsChanged");

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs) =>
        QueueLifecycleRecovery($"UserPreference:{eventArgs.Category}");

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemParameters.HighContrast) or
            nameof(SystemParameters.WorkArea))
        {
            QueueLifecycleRecovery($"SystemParameter:{eventArgs.PropertyName}");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            Log(
                QuickPodsLogLevel.Information,
                "SystemSuspending",
                "Windows is suspending; the visible product window was hidden.");
            _ = Dispatcher.InvokeAsync(() => MainWindow?.Hide());
            return;
        }

        QueueLifecycleRecovery($"Power:{eventArgs.Mode}");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason is SessionSwitchReason.SessionLock or
            SessionSwitchReason.ConsoleDisconnect or
            SessionSwitchReason.RemoteConnect)
        {
            _ = Dispatcher.InvokeAsync(() => MainWindow?.Hide());
        }

        QueueLifecycleRecovery($"Session:{eventArgs.Reason}");
    }

    private void QueueLifecycleRecovery(string reason)
    {
        if (disposed)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (disposed || lifecycleRecoveryTimer is null)
            {
                return;
            }

            _ = pendingLifecycleReasons.Add(reason);
            lifecycleRecoveryTimer.Stop();
            lifecycleRecoveryTimer.Start();
        });
    }

    private async void OnLifecycleRecoveryTick(object? sender, EventArgs eventArgs)
    {
        lifecycleRecoveryTimer?.Stop();
        if (lifecycleRecoveryRunning || disposed)
        {
            lifecycleRecoveryTimer?.Start();
            return;
        }

        lifecycleRecoveryRunning = true;
        string[] reasons = [.. pendingLifecycleReasons];
        pendingLifecycleReasons.Clear();
        try
        {
            taskbarHost?.RequestEnvironmentRecovery();
            ApplyTheme(productSettings.Theme);
            if (MainWindow is MainWindow window)
            {
                await window.RecoverAfterSystemChangeAsync();
            }

            taskbarHost?.Publish(CreateTaskbarSnapshot(
                controller?.State ?? AudioState.Unavailable));
            Log(
                QuickPodsLogLevel.Information,
                "SystemLifecycleRecovered",
                "QuickPods re-evaluated Windows audio, Bluetooth, theme, and placement state.",
                new Dictionary<string, object?>
                {
                    ["Reasons"] = string.Join(",", reasons),
                    ["HighContrast"] = SystemParameters.HighContrast,
                });
        }
        catch (Exception exception)
        {
            Log(
                QuickPodsLogLevel.Warning,
                "SystemLifecycleRecoveryFailed",
                "QuickPods contained a lifecycle recovery failure.",
                new Dictionary<string, object?>
                {
                    ["FailureType"] = exception.GetType().Name,
                });
            if (MainWindow is MainWindow window)
            {
                window.ReportSettingsFailure(
                    "Windows環境の変更後に状態を更新できませんでした。更新ボタンで再試行できます。");
            }
        }
        finally
        {
            lifecycleRecoveryRunning = false;
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (window is MainWindow productWindow)
        {
            productWindow.PositionAbovePrimaryTaskbar();
        }

        _ = window.Activate();
    }

    private void ExitApplication()
    {
        if (lifetimePolicy.IsExitRequested)
        {
            return;
        }

        lifetimePolicy.RequestExit();
        MainWindow?.Close();
        Shutdown();
    }

    private TaskbarStateSnapshot CreateTaskbarSnapshot(
        AudioState audio,
        BluetoothAudioCatalogSnapshot? catalog = null,
        BluetoothOperationSnapshot? operation = null)
    {
        BluetoothAudioCatalogSnapshot currentCatalog =
            catalog ?? bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty;
        BluetoothAudioDeviceDescriptor? selected = currentCatalog.Devices.FirstOrDefault(
            device => device.IsSelected);
        BluetoothOperationSnapshot currentOperation =
            operation ?? bluetoothOperations?.State ?? BluetoothOperationSnapshot.Idle;
        TaskbarDeviceView? selectedView = selected is null
            ? null
            : new TaskbarDeviceView(
                selected.DeviceKey.Value,
                selected.DisplayName,
                CreateBluetoothStatus(
                    selected,
                    currentCatalog.InventoryGeneration,
                    currentOperation));
        return new(
            productSettings.DisplayMode == QuickPodsDisplayMode.TrayOnly
                ? TaskbarSurfaceMode.Hidden
                : TaskbarSurfaceMode.Native,
            Math.Clamp(audio.VolumePercent, 0, 100),
            audio.IsMuted,
            selectedView);
    }

    private void ApplyTheme(QuickPodsThemeMode theme)
    {
        if (SystemParameters.HighContrast)
        {
            SetBrushColor("WindowBrush", WpfSystemColors.WindowColor);
            SetBrushColor("PanelBrush", WpfSystemColors.WindowColor);
            SetBrushColor("PanelBorderBrush", WpfSystemColors.WindowTextColor);
            SetBrushColor("ControlSurfaceBrush", WpfSystemColors.ControlColor);
            SetBrushColor("TrackBrush", WpfSystemColors.GrayTextColor);
            SetBrushColor("PrimaryTextBrush", WpfSystemColors.WindowTextColor);
            SetBrushColor("SecondaryTextBrush", WpfSystemColors.WindowTextColor);
            SetBrushColor("AccentBrush", WpfSystemColors.HighlightColor);
            SetBrushColor("HoverBrush", WpfSystemColors.ControlColor);
            SetBrushColor("SelectedBrush", WpfSystemColors.ControlColor);
            SetBrushColor("PressedBrush", WpfSystemColors.HotTrackColor);
            SetBrushColor("FocusBrush", WpfSystemColors.HighlightColor);
            SetBrushColor("ErrorBrush", WpfSystemColors.WindowTextColor);
            SetBrushColor("PrimaryButtonTextBrush", WpfSystemColors.HighlightTextColor);
            return;
        }

        bool useLightTheme = theme == QuickPodsThemeMode.Light ||
            (theme == QuickPodsThemeMode.System && IsWindowsAppsLightTheme());
        if (useLightTheme)
        {
            SetBrushColor("WindowBrush", MediaColor.FromRgb(0xF5, 0xF7, 0xFA));
            SetBrushColor("PanelBrush", Colors.White);
            SetBrushColor("PanelBorderBrush", MediaColor.FromRgb(0xD5, 0xDA, 0xE1));
            SetBrushColor("ControlSurfaceBrush", MediaColor.FromRgb(0xE9, 0xED, 0xF2));
            SetBrushColor("TrackBrush", MediaColor.FromRgb(0xCB, 0xD1, 0xD9));
            SetBrushColor("PrimaryTextBrush", MediaColor.FromRgb(0x15, 0x18, 0x1C));
            SetBrushColor("SecondaryTextBrush", MediaColor.FromRgb(0x5A, 0x62, 0x6C));
            SetBrushColor("AccentBrush", MediaColor.FromRgb(0x32, 0xB9, 0xD0));
            SetBrushColor("HoverBrush", MediaColor.FromRgb(0xE1, 0xE6, 0xEC));
            SetBrushColor("SelectedBrush", MediaColor.FromRgb(0xD9, 0xF4, 0xF8));
            SetBrushColor("PressedBrush", MediaColor.FromRgb(0xCB, 0xD2, 0xDA));
            SetBrushColor("FocusBrush", MediaColor.FromRgb(0x00, 0x6C, 0x80));
            SetBrushColor("ErrorBrush", MediaColor.FromRgb(0xA4, 0x26, 0x2C));
            SetBrushColor("PrimaryButtonTextBrush", MediaColor.FromRgb(0x10, 0x21, 0x26));
            return;
        }

        SetBrushColor("WindowBrush", MediaColor.FromRgb(0x17, 0x19, 0x1D));
        SetBrushColor("PanelBrush", MediaColor.FromRgb(0x20, 0x23, 0x28));
        SetBrushColor("PanelBorderBrush", MediaColor.FromRgb(0x36, 0x3A, 0x41));
        SetBrushColor("ControlSurfaceBrush", MediaColor.FromRgb(0x2B, 0x2E, 0x34));
        SetBrushColor("TrackBrush", MediaColor.FromRgb(0x48, 0x4D, 0x55));
        SetBrushColor("PrimaryTextBrush", MediaColor.FromRgb(0xF5, 0xF7, 0xFA));
        SetBrushColor("SecondaryTextBrush", MediaColor.FromRgb(0xAE, 0xB5, 0xBF));
        SetBrushColor("AccentBrush", MediaColor.FromRgb(0x67, 0xD7, 0xEA));
        SetBrushColor("HoverBrush", MediaColor.FromRgb(0x36, 0x3A, 0x41));
        SetBrushColor("SelectedBrush", MediaColor.FromRgb(0x1E, 0x30, 0x35));
        SetBrushColor("PressedBrush", MediaColor.FromRgb(0x40, 0x45, 0x4D));
        SetBrushColor("FocusBrush", MediaColor.FromRgb(0xB9, 0xF4, 0xFF));
        SetBrushColor("ErrorBrush", MediaColor.FromRgb(0xFF, 0xB4, 0xA9));
        SetBrushColor("PrimaryButtonTextBrush", MediaColor.FromRgb(0x10, 0x21, 0x26));
    }

    private void SetBrushColor(string resourceKey, MediaColor color)
    {
        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private static bool IsWindowsAppsLightTheme()
    {
        try
        {
            using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return personalize?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateBluetoothStatus(
        BluetoothAudioDeviceDescriptor selected,
        long inventoryGeneration,
        BluetoothOperationSnapshot operation)
    {
        if (operation.Target is { } target &&
            target.DeviceKey == selected.DeviceKey &&
            target.InventoryGeneration == inventoryGeneration)
        {
            if (operation.Operation == QuickPodsOperation.Connecting)
            {
                return "接続中";
            }

            if (operation.Operation == QuickPodsOperation.SettingDefault)
            {
                return "既定出力へ切替中";
            }

            if (operation.Operation == QuickPodsOperation.Disconnecting)
            {
                return "切断中";
            }

            if (operation.Outcome == BluetoothOperationOutcome.ConnectedNotDefault)
            {
                return "接続済み・非既定";
            }

            if (operation.ConnectionState == BluetoothConnectionState.Connected)
            {
                return operation.DefaultOutputState == DefaultOutputState.Default
                    ? "接続済み・既定"
                    : "接続済み";
            }

            if (operation.ConnectionState == BluetoothConnectionState.Disconnected)
            {
                return "未接続";
            }

            if (operation.Outcome == BluetoothOperationOutcome.Unsupported)
            {
                return "直接操作は未対応";
            }
        }

        return selected.ConnectionState switch
        {
            BluetoothConnectionState.Connected => "接続済み",
            BluetoothConnectionState.Disconnected => "未接続",
            BluetoothConnectionState.Unavailable => "利用不可",
            _ => "確認中",
        };
    }

    private sealed class UnavailableAudioEndpointPort : IAudioEndpointPort
    {
        private static readonly AudioState Unavailable = AudioState.Unavailable with
        {
            Capability = AudioCapability.ServiceUnavailable,
        };

        public event EventHandler<AudioStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Unavailable);

        public ValueTask<AudioState> SetVolumeAsync(
            int volumePercent,
            CancellationToken cancellationToken) => ValueTask.FromResult(Unavailable);

        public ValueTask<AudioState> SetMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken) => ValueTask.FromResult(Unavailable);
    }
}
