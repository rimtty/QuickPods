using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
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
    private QuickPodsSettings productSettings = QuickPodsSettings.Default;
    private bool productSettingsInitialized;
    private bool disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        taskbarHost.Start(CreateTaskbarSnapshot(controller.State));

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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimePolicy.RequestExit();
        Log(QuickPodsLogLevel.Information, "ApplicationStopping", "QuickPods is stopping.");
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
        bluetoothSelectionStore = new JsonBluetoothSelectionStore(
            new JsonSettingsStore<QuickPodsSettings>(settingsPath));
    }

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

    private void OnHostInteractionReceived(object? sender, HostInteractionEnvelope interaction)
    {
        _ = Dispatcher.InvokeAsync(() => HandleHostInteractionAsync(interaction)).Task.Unwrap();
    }

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
    }

    private void SetBrushColor(string resourceKey, MediaColor color)
    {
        if (Resources[resourceKey] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
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
