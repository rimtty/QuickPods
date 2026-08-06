using System.IO;
using System.Windows;
using QuickPods.Contracts;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Runtime;
using QuickPods.Infrastructure.Settings;
using QuickPods.Windows.Audio;
using QuickPods.Windows.Bluetooth;

namespace QuickPods.App;

public partial class App : Application, IDisposable
{
    private AudioController? controller;
    private BluetoothCatalogController? bluetoothCatalog;
    private WindowsBluetoothAudioCatalogPort? bluetoothCatalogPort;
    private JsonBluetoothSelectionStore? bluetoothSelectionStore;
    private CancellationTokenSource? bluetoothLifetime;
    private Task? bluetoothInitialization;
    private TaskbarHostProcessManager? taskbarHost;
    private WindowsCoreAudioEndpointPort? windowsAudio;
    private bool disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        MainWindow = new MainWindow(controller, startupDiagnostic);
        MainWindow.Show();

        string hostExecutable = Path.Combine(AppContext.BaseDirectory, "QuickPods.TaskbarHost.exe");
        taskbarHost = new TaskbarHostProcessManager(hostExecutable);
        controller.StateChanged += OnAudioStateChanged;
        taskbarHost.InteractionReceived += OnHostInteractionReceived;
        taskbarHost.Start(CreateTaskbarSnapshot(controller.State));

        StartBluetoothCatalog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        bluetoothLifetime?.Cancel();
        if (controller is not null)
        {
            controller.StateChanged -= OnAudioStateChanged;
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

        try
        {
            bluetoothInitialization?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bluetoothCatalog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bluetoothSelectionStore?.Dispose();
        bluetoothCatalogPort?.Dispose();
        bluetoothLifetime?.Dispose();
        windowsAudio?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs) =>
        taskbarHost?.Publish(CreateTaskbarSnapshot(eventArgs.State));

    private void OnBluetoothCatalogStateChanged(
        object? sender,
        BluetoothAudioCatalogSnapshot catalog) =>
        taskbarHost?.Publish(CreateTaskbarSnapshot(controller?.State ?? AudioState.Unavailable, catalog));

    private void StartBluetoothCatalog()
    {
        try
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickPods",
                "settings.json");
            bluetoothCatalogPort = new WindowsBluetoothAudioCatalogPort();
            bluetoothSelectionStore = new JsonBluetoothSelectionStore(
                new JsonSettingsStore<QuickPodsSettings>(settingsPath));
            bluetoothCatalog = new BluetoothCatalogController(
                bluetoothCatalogPort,
                bluetoothSelectionStore);
            bluetoothCatalog.StateChanged += OnBluetoothCatalogStateChanged;
            bluetoothLifetime = new CancellationTokenSource();
            bluetoothInitialization = InitializeBluetoothCatalogAsync(bluetoothLifetime.Token);
        }
        catch
        {
            bluetoothCatalog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            bluetoothSelectionStore?.Dispose();
            bluetoothCatalogPort?.Dispose();
            bluetoothLifetime?.Dispose();
            bluetoothCatalog = null;
            bluetoothSelectionStore = null;
            bluetoothCatalogPort = null;
            bluetoothLifetime = null;
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
        catch
        {
            // Catalog faults do not disable master-volume control or terminate the app.
        }
    }

    private void OnHostInteractionReceived(object? sender, HostInteractionEnvelope interaction)
    {
        _ = Dispatcher.InvokeAsync(() => HandleHostInteractionAsync(interaction)).Task.Unwrap();
    }

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

        _ = window.Activate();
    }

    private TaskbarStateSnapshot CreateTaskbarSnapshot(
        AudioState audio,
        BluetoothAudioCatalogSnapshot? catalog = null)
    {
        BluetoothAudioCatalogSnapshot currentCatalog =
            catalog ?? bluetoothCatalog?.State ?? BluetoothAudioCatalogSnapshot.Empty;
        BluetoothAudioDeviceDescriptor? selected = currentCatalog.Devices.FirstOrDefault(
            device => device.IsSelected);
        TaskbarDeviceView? selectedView = selected is null
            ? null
            : new TaskbarDeviceView(
                selected.DeviceKey.Value,
                selected.DisplayName,
                selected.ConnectionState switch
                {
                    BluetoothConnectionState.Connected => "接続済み",
                    BluetoothConnectionState.Disconnected => "未接続",
                    BluetoothConnectionState.Unavailable => "利用不可",
                    _ => "確認中",
                });
        return new(
            TaskbarSurfaceMode.Native,
            Math.Clamp(audio.VolumePercent, 0, 100),
            audio.IsMuted,
            selectedView);
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
