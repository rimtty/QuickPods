using System.Windows;
using System.Windows.Input;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.App;

public partial class MainWindow : Window
{
    private const int MouseWheelStepPercent = 2;
    private readonly AudioController controller;
    private bool applyingState;

    public MainWindow(AudioController controller, string? startupDiagnostic)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        InitializeComponent();
        DiagnosticText.Text = startupDiagnostic ?? string.Empty;
        controller.StateChanged += OnAudioStateChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await RunOperationAsync(() => controller.InitializeAsync().AsTask());
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        controller.StateChanged -= OnAudioStateChanged;
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (applyingState || !VolumeSlider.IsEnabled)
        {
            return;
        }

        int value = (int)Math.Round(eventArgs.NewValue, MidpointRounding.AwayFromZero);
        VolumePercentText.Text = $"{value}%";
        controller.PreviewVolume(value);
    }

    private async void OnVolumeCommit(object sender, MouseButtonEventArgs eventArgs)
    {
        int value = (int)Math.Round(VolumeSlider.Value, MidpointRounding.AwayFromZero);
        await RunOperationAsync(() => controller.CommitVolumeAsync(value).AsTask());
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
        await RunOperationAsync(() => controller.CommitVolumeAsync(value).AsTask());
    }

    private async void OnToggleMute(object sender, RoutedEventArgs eventArgs)
    {
        await RunOperationAsync(() => controller.ToggleMuteAsync().AsTask());
    }

    private async void OnRefresh(object sender, RoutedEventArgs eventArgs)
    {
        await RunOperationAsync(() => controller.InitializeAsync().AsTask());
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyState(eventArgs.State));
    }

    private void ApplyState(AudioState state)
    {
        applyingState = true;
        try
        {
            bool available = state.Capability == AudioCapability.Available;
            EndpointNameText.Text = state.EndpointDisplayName ?? "利用可能な出力デバイスがありません";
            VolumeSlider.IsEnabled = available;
            MuteButton.IsEnabled = available;
            VolumeSlider.Value = state.VolumePercent;
            VolumePercentText.Text = available ? $"{state.VolumePercent}%" : "--%";
            MuteButton.ToolTip = state.IsMuted ? "ミュート解除" : "ミュート";
            MuteGlyph.Text = state.IsMuted ? "\uE74F" : "\uE767";
            StatusText.Text = state.Capability switch
            {
                AudioCapability.Available => state.IsMuted ? "ミュート中" : "利用可能",
                AudioCapability.ServiceUnavailable => "Audio service利用不可",
                _ => "出力デバイスなし",
            };
        }
        finally
        {
            applyingState = false;
        }
    }

    private async Task RunOperationAsync(Func<Task<AudioState>> operation)
    {
        try
        {
            AudioState state = await operation();
            ApplyState(state);
            DiagnosticText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            DiagnosticText.Text = exception.Message;
            ApplyState(AudioState.Unavailable);
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            DiagnosticText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            DiagnosticText.Text = exception.Message;
        }
    }
}
