using QuickPods.Contracts;

namespace QuickPods.TaskbarHost.Hosting;

internal readonly record struct NativeSliderInteractionResult(
    HostInteractionEnvelope Envelope,
    bool StateChanged);

/// <summary>
/// Owns the input state shared by native and floating surfaces. It contains no
/// HWND operations, so both hosts produce identical protocol interactions.
/// </summary>
internal sealed class NativeSliderInteractionSession
{
    private readonly TaskbarSurfaceMode surfaceMode;
    private TaskbarStateSnapshot state;
    private long nextSequence;

    internal NativeSliderInteractionSession(TaskbarSurfaceMode surfaceMode)
    {
        if (surfaceMode is not TaskbarSurfaceMode.Native and not TaskbarSurfaceMode.Floating)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceMode),
                surfaceMode,
                "A visible slider session requires a native or floating surface mode.");
        }

        this.surfaceMode = surfaceMode;
        state = new TaskbarStateSnapshot(surfaceMode, 0, false, null);
    }

    internal TaskbarStateSnapshot State => state;

    internal bool IsDragging { get; private set; }

    internal bool SetState(TaskbarStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TaskbarStateSnapshot normalized = snapshot with
        {
            SurfaceMode = surfaceMode,
            VolumePercent = Math.Clamp(snapshot.VolumePercent, 0, 100),
        };
        if (normalized == state)
        {
            return false;
        }

        state = normalized;
        return true;
    }

    internal static bool CanBegin(SliderLayout layout, nint messagePosition) =>
        SliderGeometry.ContainsPointer(
            layout,
            SignedLowWord(messagePosition),
            SignedHighWord(messagePosition));

    internal bool TryBegin(
        SliderLayout layout,
        nint messagePosition,
        out NativeSliderInteractionResult result)
    {
        int pointerX = SignedLowWord(messagePosition);
        if (!CanBegin(layout, messagePosition))
        {
            result = default;
            return false;
        }

        IsDragging = true;
        return TryCreate(
            HostInteractionKind.SetVolumePreview,
            HostInteractionCalculator.VolumePercentFromPointer(layout, pointerX),
            out result);
    }

    internal bool TryMove(
        SliderLayout layout,
        nint messagePosition,
        out NativeSliderInteractionResult result)
    {
        if (!IsDragging)
        {
            result = default;
            return false;
        }

        return TryCreate(
            HostInteractionKind.SetVolumePreview,
            HostInteractionCalculator.VolumePercentFromPointer(
                layout,
                SignedLowWord(messagePosition)),
            out result);
    }

    internal bool TryComplete(
        SliderLayout layout,
        nint messagePosition,
        out NativeSliderInteractionResult result)
    {
        if (!IsDragging)
        {
            result = default;
            return false;
        }

        IsDragging = false;
        return TryCreate(
            HostInteractionKind.SetVolumeCommit,
            HostInteractionCalculator.VolumePercentFromPointer(
                layout,
                SignedLowWord(messagePosition)),
            out result);
    }

    internal bool TryWheel(nuint wParam, out NativeSliderInteractionResult result) =>
        TryCreate(
            HostInteractionKind.SetVolumeCommit,
            HostInteractionCalculator.VolumePercentFromWheel(
                state.VolumePercent,
                SignedHighWord(unchecked((nint)wParam))),
            out result);

    internal bool CancelDrag()
    {
        bool wasDragging = IsDragging;
        IsDragging = false;
        return wasDragging;
    }

    internal void Reset()
    {
        state = new TaskbarStateSnapshot(surfaceMode, 0, false, null);
        nextSequence = 0;
        IsDragging = false;
    }

    private bool TryCreate(
        HostInteractionKind kind,
        int volumePercent,
        out NativeSliderInteractionResult result)
    {
        if (nextSequence == long.MaxValue)
        {
            result = default;
            return false;
        }

        int normalized = Math.Clamp(volumePercent, 0, 100);
        TaskbarStateSnapshot updated = state with { VolumePercent = normalized };
        bool stateChanged = updated != state;
        state = updated;
        result = new(
            new HostInteractionEnvelope(
                QuickPodsProtocol.Version,
                nextSequence++,
                kind,
                normalized),
            stateChanged);
        return true;
    }

    private static int SignedLowWord(nint value) =>
        unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) =>
        unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
}
