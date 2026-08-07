using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Hosting;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class NativeTaskbarHostContractTests
{
    [Fact]
    public async Task AutomationMtaReusesOneDedicatedWorkerAcrossCompletedScans()
    {
        var workerThreadIds = new HashSet<int>();

        for (int iteration = 0; iteration < 8; iteration++)
        {
            int workerThreadId = await AutomationMta.RunAsync(
                () =>
                {
                    Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
                    return Environment.CurrentManagedThreadId;
                },
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            workerThreadIds.Add(workerThreadId);
        }

        Assert.Single(workerThreadIds);
    }

    [Fact]
    public void PopupPreservedStyleForbidsChildAndTopmostBits()
    {
        Assert.True(NativeWindowStyles.MatchesPopupPreserved(NativeWindowStyles.PopupPreservedStyle));
        Assert.True(NativeWindowStyles.MatchesPopupPreserved(
            NativeWindowStyles.PopupPreservedStyle | NativeWindowStyles.WindowStyleVisible));
        Assert.False(NativeWindowStyles.MatchesPopupPreserved(
            NativeWindowStyles.PopupPreservedStyle | NativeWindowStyles.WindowStyleChild));

        Assert.True(NativeWindowStyles.MatchesRequiredExtendedStyle(
            NativeWindowStyles.RequiredExtendedStyle));
        Assert.False(NativeWindowStyles.MatchesRequiredExtendedStyle(
            NativeWindowStyles.RequiredExtendedStyle | NativeWindowStyles.WindowExtendedStyleTopmost));
    }

    [Fact]
    public void InteractionCalculatorClampsPointerAndWheelVolume()
    {
        Assert.True(SliderGeometry.TryCreate(300, 40, out SliderLayout layout));
        Assert.Equal(0, HostInteractionCalculator.VolumePercentFromPointer(layout, layout.TrackLeft - 20));
        Assert.Equal(100, HostInteractionCalculator.VolumePercentFromPointer(layout, layout.TrackRight + 20));
        Assert.Equal(2, HostInteractionCalculator.VolumePercentFromWheel(0, 120));
        Assert.Equal(100, HostInteractionCalculator.VolumePercentFromWheel(100, 120));
        Assert.Equal(0, HostInteractionCalculator.VolumePercentFromWheel(0, -120));

        var session = new NativeSliderInteractionSession(TaskbarSurfaceMode.Floating);
        Assert.True(session.SetState(new(TaskbarSurfaceMode.Native, 25, false, null)));
        Assert.True(session.TryBegin(
            layout,
            PointMessage(layout.TrackLeft, layout.CenterY),
            out NativeSliderInteractionResult started));
        Assert.True(session.TryMove(
            layout,
            PointMessage(layout.TrackRight, layout.CenterY),
            out NativeSliderInteractionResult moved));
        int midpoint = layout.TrackLeft + ((layout.TrackRight - layout.TrackLeft) / 2);
        Assert.True(session.TryComplete(
            layout,
            PointMessage(midpoint, layout.CenterY),
            out NativeSliderInteractionResult completed));
        Assert.True(session.TryWheel(WheelMessage(-120), out NativeSliderInteractionResult wheel));
        Assert.True(session.TryInvokePrimary(
            layout,
            PointMessage(layout.IconLeft, layout.CenterY),
            out NativeSliderInteractionResult mute));
        Assert.True(session.TryInvokePrimary(
            layout,
            PointMessage(0, 0),
            out NativeSliderInteractionResult flyout));
        Assert.True(session.TryOpenContextMenu(out NativeSliderInteractionResult contextMenu));
        Assert.True(session.TryPreviewFlyout(out NativeSliderInteractionResult preview));
        Assert.True(session.TryNotifyPointerExited(out NativeSliderInteractionResult pointerExited));

        var hover = new NativeHoverInteractionSession();
        Assert.True(hover.TryArm());
        Assert.False(hover.TryArm());
        Assert.True(hover.TryRequestPreview());
        Assert.False(hover.TryRequestPreview());
        Assert.True(hover.Reset());
        Assert.False(hover.TryRequestPreview());

        Assert.Equal(TaskbarSurfaceMode.Floating, session.State.SurfaceMode);
        Assert.Equal((0L, HostInteractionKind.SetVolumePreview, 0),
            (started.Envelope.Sequence, started.Envelope.Kind, started.Envelope.VolumePercent));
        Assert.Equal((1L, HostInteractionKind.SetVolumePreview, 100),
            (moved.Envelope.Sequence, moved.Envelope.Kind, moved.Envelope.VolumePercent));
        Assert.Equal((2L, HostInteractionKind.SetVolumeCommit, 50),
            (completed.Envelope.Sequence, completed.Envelope.Kind, completed.Envelope.VolumePercent));
        Assert.Equal((3L, HostInteractionKind.SetVolumeCommit, 48),
            (wheel.Envelope.Sequence, wheel.Envelope.Kind, wheel.Envelope.VolumePercent));
        Assert.Equal((4L, HostInteractionKind.ToggleMute, null),
            (mute.Envelope.Sequence, mute.Envelope.Kind, mute.Envelope.VolumePercent));
        Assert.True(session.State.IsMuted);
        Assert.Equal((5L, HostInteractionKind.OpenAudioFlyout, null),
            (flyout.Envelope.Sequence, flyout.Envelope.Kind, flyout.Envelope.VolumePercent));
        Assert.Equal((6L, HostInteractionKind.OpenContextMenu, null),
            (contextMenu.Envelope.Sequence, contextMenu.Envelope.Kind, contextMenu.Envelope.VolumePercent));
        Assert.Equal((7L, HostInteractionKind.PreviewAudioFlyout),
            (preview.Envelope.Sequence, preview.Envelope.Kind));
        Assert.Equal((8L, HostInteractionKind.TaskbarPointerExited),
            (pointerExited.Envelope.Sequence, pointerExited.Envelope.Kind));
        Assert.False(session.IsDragging);
    }

    private static nint PointMessage(int x, int y) =>
        unchecked((nint)(((uint)(ushort)y << 16) | (ushort)x));

    private static nuint WheelMessage(short delta) =>
        unchecked((nuint)((uint)(ushort)delta << 16));
}
