using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Core;

public sealed class AudioController : IAsyncDisposable
{
    private readonly IAudioEndpointPort audio;
    private readonly LatestVolumeWriter volumeWriter;
    private readonly object stateLock = new();
    private AudioState state = AudioState.Unavailable;
    private bool disposed;

    public AudioController(
        IAudioEndpointPort audio,
        TimeSpan? writeInterval = null,
        TimeProvider? timeProvider = null)
    {
        this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
        volumeWriter = new LatestVolumeWriter(
            WriteVolumeAsync,
            writeInterval ?? TimeSpan.FromMilliseconds(20),
            timeProvider ?? TimeProvider.System);
        audio.StateChanged += HandleAudioStateChanged;
    }

    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    public AudioState State
    {
        get
        {
            lock (stateLock)
            {
                return state;
            }
        }
    }

    public async ValueTask<AudioState> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        AudioState snapshot = await audio.ReadAsync(cancellationToken).ConfigureAwait(false);
        return PublishIfCurrent(snapshot, isSelfOriginated: false);
    }

    public void PreviewVolume(int volumePercent)
    {
        ThrowIfDisposed();
        int clamped = Math.Clamp(volumePercent, 0, 100);
        AudioState preview;
        lock (stateLock)
        {
            if (state.Capability != AudioCapability.Available)
            {
                return;
            }

            preview = state with { VolumePercent = clamped };
            state = preview;
        }

        StateChanged?.Invoke(this, new AudioStateChangedEventArgs(preview, isSelfOriginated: true));
        volumeWriter.Publish(clamped);
    }

    public ValueTask CommitVolumeAsync(
        int volumePercent,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        int clamped = Math.Clamp(volumePercent, 0, 100);
        PreviewVolume(clamped);
        lock (stateLock)
        {
            if (state.Capability != AudioCapability.Available)
            {
                return ValueTask.CompletedTask;
            }
        }

        return volumeWriter.FlushAsync(clamped, cancellationToken);
    }

    public async ValueTask<AudioState> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        bool nextMute;
        lock (stateLock)
        {
            nextMute = !state.IsMuted;
        }

        AudioState snapshot = await audio.SetMuteAsync(nextMute, cancellationToken).ConfigureAwait(false);
        return PublishIfCurrent(snapshot, isSelfOriginated: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        audio.StateChanged -= HandleAudioStateChanged;
        await volumeWriter.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask WriteVolumeAsync(int volumePercent, CancellationToken cancellationToken)
    {
        AudioState snapshot = await audio.SetVolumeAsync(volumePercent, cancellationToken).ConfigureAwait(false);
        if (snapshot.Capability != AudioCapability.Available)
        {
            PublishIfCurrent(snapshot, isSelfOriginated: true);
        }
    }

    private void HandleAudioStateChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        if (eventArgs.IsSelfOriginated)
        {
            return;
        }

        PublishIfCurrent(eventArgs.State, eventArgs.IsSelfOriginated);
    }

    private AudioState PublishIfCurrent(AudioState snapshot, bool isSelfOriginated)
    {
        bool accepted;
        lock (stateLock)
        {
            accepted = snapshot.Generation >= state.Generation;
            if (accepted)
            {
                state = snapshot;
            }
        }

        if (accepted)
        {
            StateChanged?.Invoke(this, new AudioStateChangedEventArgs(snapshot, isSelfOriginated));
        }

        return State;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

internal sealed class LatestVolumeWriter : IAsyncDisposable
{
    private readonly Func<int, CancellationToken, ValueTask> writer;
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;
    private readonly object sync = new();
    private readonly CancellationTokenSource shutdown = new();
    private Task worker = Task.CompletedTask;
    private int? pending;
    private bool disposed;

    public LatestVolumeWriter(
        Func<int, CancellationToken, ValueTask> writer,
        TimeSpan interval,
        TimeProvider timeProvider)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        this.interval = interval;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void Publish(int value)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending = value;
            if (worker.IsCompleted)
            {
                worker = RunAsync();
            }
        }
    }

    public async ValueTask FlushAsync(int value, CancellationToken cancellationToken)
    {
        Publish(value);

        while (true)
        {
            Task current;
            lock (sync)
            {
                current = worker;
            }

            await current.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (sync)
            {
                if (pending is null && worker.IsCompleted)
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task current;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = worker;
        }

        shutdown.Cancel();
        try
        {
            await current.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await Task.Delay(interval, timeProvider, shutdown.Token).ConfigureAwait(false);

            int value;
            lock (sync)
            {
                if (pending is not { } latest)
                {
                    return;
                }

                value = latest;
                pending = null;
            }

            await writer(value, shutdown.Token).ConfigureAwait(false);

            lock (sync)
            {
                if (pending is null)
                {
                    worker = Task.CompletedTask;
                    return;
                }
            }
        }
    }
}
