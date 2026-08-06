using System.IO;
using System.Windows;
using QuickPods.Contracts;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Runtime;
using QuickPods.Windows.Audio;

namespace QuickPods.App;

public partial class App : Application, IDisposable
{
    private AudioController? controller;
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
        if (controller is not null)
        {
            controller.StateChanged -= OnAudioStateChanged;
        }

        if (taskbarHost is not null)
        {
            taskbarHost.InteractionReceived -= OnHostInteractionReceived;
            taskbarHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        windowsAudio?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs) =>
        taskbarHost?.Publish(CreateTaskbarSnapshot(eventArgs.State));

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

    private static TaskbarStateSnapshot CreateTaskbarSnapshot(AudioState audio) =>
        new(
            TaskbarSurfaceMode.Native,
            Math.Clamp(audio.VolumePercent, 0, 100),
            audio.IsMuted,
            null);

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
