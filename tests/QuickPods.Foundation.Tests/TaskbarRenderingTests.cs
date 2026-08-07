using QuickPods.Contracts;
using QuickPods.TaskbarHost.Hosting;
using QuickPods.TaskbarHost.Interop;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarRenderingTests
{
    [Theory]
    [InlineData(300, 40)]
    [InlineData(600, 80)]
    public void SliderGeometryDrawingAndInputShareTrack(int width, int height)
    {
        Assert.True(SliderGeometry.TryCreate(width, height, out SliderLayout layout));

        Assert.Equal(0d, SliderGeometry.FractionFromPointerX(layout, layout.TrackLeft));
        Assert.Equal(1d, SliderGeometry.FractionFromPointerX(layout, layout.TrackRight));

        int midpoint = layout.TrackLeft + ((layout.TrackRight - layout.TrackLeft) / 2);
        Assert.InRange(SliderGeometry.FractionFromPointerX(layout, midpoint), 0.49d, 0.51d);
        Assert.True(SliderGeometry.ContainsPointer(layout, midpoint, layout.CenterY));
        Assert.False(SliderGeometry.ContainsPointer(layout, layout.TrackLeft - 1, layout.CenterY));
        int speakerCenter = layout.IconLeft + (layout.IconSize / 2);
        Assert.True(SliderGeometry.ContainsSpeakerPointer(layout, speakerCenter, layout.CenterY));
        Assert.False(SliderGeometry.ContainsSpeakerPointer(
            layout,
            layout.IconLeft + layout.IconSize,
            layout.CenterY));
    }

    [Fact]
    public void RenderingBoundaryRejectsInvalidSurfaceAndNormalizesContractVolume()
    {
        Assert.False(SliderGeometry.TryCreate(1, 1, out _));

        TaskbarRenderState low = TaskbarRenderState.FromSnapshot(
            new(TaskbarSurfaceMode.Native, -1, false, null));
        TaskbarRenderState highMuted = TaskbarRenderState.FromSnapshot(
            new(
                TaskbarSurfaceMode.Native,
                101,
                true,
                new TaskbarDeviceView("device", "AirPods Pro", "未接続")));
        TaskbarRenderState connected = TaskbarRenderState.FromSnapshot(
            new(
                TaskbarSurfaceMode.Native,
                42,
                false,
                new TaskbarDeviceView("device", "AirPods Pro", "接続済み・既定")));

        Assert.Equal(0d, low.VolumeFraction);
        Assert.Equal(1d, highMuted.VolumeFraction);
        Assert.True(highMuted.IsMuted);
        Assert.Equal("BTデバイスなし", low.DeviceDisplayName);
        Assert.False(low.HasSelectedDevice);
        Assert.Equal("AirPods Pro", highMuted.DeviceDisplayName);
        Assert.Equal("未接続", highMuted.DeviceStatusText);
        Assert.Equal("未接続 · AirPods Pro", highMuted.DeviceLabel);
        Assert.False(highMuted.HasActiveDeviceConnection);
        Assert.True(highMuted.HasSelectedDevice);
        Assert.Equal("AirPods Pro", connected.DeviceLabel);
        Assert.True(connected.HasActiveDeviceConnection);
        Assert.Equal("\uE767", FluentAudioGlyphs.ForMuteState(isMuted: false));
        Assert.Equal("\uE74F", FluentAudioGlyphs.ForMuteState(isMuted: true));
        Assert.Equal("\uE7F6", FluentAudioGlyphs.Headphones);
        Assert.Equal("Segoe Fluent Icons", FluentAudioGlyphs.FontFamily);
    }

    [Fact]
    public void ResolvedThemeSelectsDeterministicNativePalette()
    {
        TaskbarRenderTheme dark = TaskbarRenderTheme.Resolve(TaskbarThemeMode.Dark);
        TaskbarRenderTheme light = TaskbarRenderTheme.Resolve(TaskbarThemeMode.Light);
        TaskbarRenderTheme highContrast = TaskbarRenderTheme.Resolve(
            TaskbarThemeMode.HighContrast);

        Assert.Equal(0x0024211Fu, dark.SurfaceColor);
        Assert.Equal(0x00F4F2F0u, dark.ForegroundColor);
        Assert.Equal(0x00FAF7F5u, light.SurfaceColor);
        Assert.Equal(0x001C1815u, light.ForegroundColor);
        Assert.NotEqual(dark, light);
        Assert.Equal(dark.TransparentColorKey, highContrast.TransparentColorKey);
        Assert.Equal(
            HostNativeMethods.GetSystemColor(HostNativeMethods.ColorWindow),
            highContrast.SurfaceColor);
        Assert.Equal(
            HostNativeMethods.GetSystemColor(HostNativeMethods.ColorWindowText),
            highContrast.ForegroundColor);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaskbarRenderTheme.Resolve((TaskbarThemeMode)99));
    }
}
