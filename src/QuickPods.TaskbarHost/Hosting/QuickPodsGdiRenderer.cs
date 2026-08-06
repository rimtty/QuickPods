using System.ComponentModel;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Interop;
using NativeRect = QuickPods.TaskbarHost.Interop.GdiNativeMethods.NativeRect;

namespace QuickPods.TaskbarHost.Hosting;

internal static class QuickPodsGdiRenderer
{
    internal static void Paint(nint windowHandle, TaskbarStateSnapshot snapshot) =>
        Paint(windowHandle, TaskbarRenderState.FromSnapshot(snapshot), TaskbarRenderTheme.Dark);

    internal static void Paint(
        nint windowHandle,
        TaskbarRenderState state,
        TaskbarRenderTheme theme)
    {
        nint deviceContext = GdiNativeMethods.BeginPaint(windowHandle, out var paint);
        if (deviceContext == nint.Zero)
        {
            throw new Win32Exception("BeginPaint failed for the QuickPods native host.");
        }

        try
        {
            if (!GdiNativeMethods.GetClientRect(windowHandle, out NativeRect client))
            {
                throw new Win32Exception();
            }

            DrawBuffered(deviceContext, client, state, theme);
        }
        finally
        {
            _ = GdiNativeMethods.EndPaint(windowHandle, ref paint);
        }
    }

    private static void DrawBuffered(
        nint destinationDeviceContext,
        NativeRect destinationBounds,
        TaskbarRenderState state,
        TaskbarRenderTheme theme)
    {
        int width = Math.Max(1, destinationBounds.Right - destinationBounds.Left);
        int height = Math.Max(1, destinationBounds.Bottom - destinationBounds.Top);

        using SafeMemoryDeviceContextHandle memoryDeviceContext =
            CreateMemoryDeviceContext(destinationDeviceContext);
        using SafeGdiObjectHandle bitmap = CreateCompatibleBitmap(
            destinationDeviceContext,
            width,
            height);

        nint previousBitmap = GdiNativeMethods.SelectObject(
            memoryDeviceContext.DangerousGetHandle(),
            bitmap.DangerousGetHandle());
        if (!IsValidSelectedObject(previousBitmap))
        {
            throw new Win32Exception("Unable to select the GDI back buffer.");
        }

        try
        {
            var bufferBounds = new NativeRect
            {
                Right = width,
                Bottom = height,
            };
            Draw(memoryDeviceContext.DangerousGetHandle(), bufferBounds, state, theme);

            if (!GdiNativeMethods.BitBlt(
                    destinationDeviceContext,
                    destinationBounds.Left,
                    destinationBounds.Top,
                    width,
                    height,
                    memoryDeviceContext.DangerousGetHandle(),
                    0,
                    0,
                    GdiNativeMethods.RasterOperationSourceCopy))
            {
                throw new Win32Exception("Unable to present the GDI back buffer.");
            }
        }
        finally
        {
            _ = GdiNativeMethods.SelectObject(
                memoryDeviceContext.DangerousGetHandle(),
                previousBitmap);
        }
    }

    private static void Draw(
        nint deviceContext,
        NativeRect bounds,
        TaskbarRenderState state,
        TaskbarRenderTheme theme)
    {
        int width = Math.Max(1, bounds.Right - bounds.Left);
        int height = Math.Max(1, bounds.Bottom - bounds.Top);
        int radius = Math.Max(6, height / 3);

        using (SafeGdiObjectHandle transparentBrush = CreateBrush(theme.TransparentColorKey))
        {
            if (GdiNativeMethods.FillRect(deviceContext, ref bounds, transparentBrush.DangerousGetHandle()) == 0)
            {
                throw new Win32Exception("Unable to clear the layered host to its transparent color key.");
            }
        }

        using SafeGdiObjectHandle surfaceBrush = CreateBrush(theme.SurfaceColor);
        using SafeGdiObjectHandle surfacePen = CreatePen(1, theme.SurfaceOutlineColor);
        WithSelectedObjects(deviceContext, surfaceBrush, surfacePen, () =>
        {
            _ = GdiNativeMethods.RoundRect(
                deviceContext,
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom,
                radius,
                radius);
        });

        if (!SliderGeometry.TryCreate(width, height, out SliderLayout layout))
        {
            return;
        }

        DrawSpeaker(deviceContext, layout, state.IsMuted, theme.ForegroundColor);
        DrawTrack(deviceContext, layout, state.VolumeFraction, height, theme);
    }

    private static void DrawTrack(
        nint deviceContext,
        SliderLayout layout,
        double volumeFraction,
        int height,
        TaskbarRenderTheme theme)
    {
        using SafeGdiObjectHandle trackBrush = CreateBrush(theme.TrackColor);
        using SafeGdiObjectHandle trackPen = CreatePen(1, theme.TrackColor);
        WithSelectedObjects(deviceContext, trackBrush, trackPen, () =>
        {
            _ = GdiNativeMethods.RoundRect(
                deviceContext,
                layout.TrackLeft,
                layout.TrackTop,
                layout.TrackRight,
                layout.TrackBottom,
                layout.TrackHeight,
                layout.TrackHeight);
        });

        int thumbX = layout.TrackLeft +
            (int)Math.Round((layout.TrackRight - layout.TrackLeft) * volumeFraction);
        using SafeGdiObjectHandle accentBrush = CreateBrush(theme.AccentColor);
        using SafeGdiObjectHandle accentPen = CreatePen(1, theme.AccentColor);
        WithSelectedObjects(deviceContext, accentBrush, accentPen, () =>
        {
            if (thumbX > layout.TrackLeft)
            {
                _ = GdiNativeMethods.RoundRect(
                    deviceContext,
                    layout.TrackLeft,
                    layout.TrackTop,
                    thumbX,
                    layout.TrackBottom,
                    layout.TrackHeight,
                    layout.TrackHeight);
            }

            int thumbRadius = Math.Clamp(height / 10, 4, 8);
            _ = GdiNativeMethods.Ellipse(
                deviceContext,
                thumbX - thumbRadius,
                layout.CenterY - thumbRadius,
                thumbX + thumbRadius + 1,
                layout.CenterY + thumbRadius + 1);
        });
    }

    private static void DrawSpeaker(
        nint deviceContext,
        SliderLayout layout,
        bool isMuted,
        uint foregroundColor)
    {
        int left = layout.IconLeft;
        int centerY = layout.CenterY;
        int size = layout.IconSize;
        int half = Math.Max(4, size / 2);
        GdiNativeMethods.NativeGdiPoint[] speaker =
        [
            new(left, centerY - (half / 2)),
            new(left + (half / 2), centerY - (half / 2)),
            new(left + half, centerY - half),
            new(left + half, centerY + half),
            new(left + (half / 2), centerY + (half / 2)),
            new(left, centerY + (half / 2)),
        ];

        using SafeGdiObjectHandle foregroundBrush = CreateBrush(foregroundColor);
        using SafeGdiObjectHandle foregroundPen = CreatePen(Math.Max(1, size / 8), foregroundColor);
        WithSelectedObjects(deviceContext, foregroundBrush, foregroundPen, () =>
        {
            _ = GdiNativeMethods.Polygon(deviceContext, speaker, speaker.Length);
            if (isMuted)
            {
                int slashLeft = left + half;
                _ = GdiNativeMethods.MoveTo(deviceContext, slashLeft, centerY - half, nint.Zero);
                _ = GdiNativeMethods.LineTo(deviceContext, left + size + half, centerY + half);
                return;
            }

            int arcLeft = left + (half / 2);
            _ = GdiNativeMethods.Arc(
                deviceContext,
                arcLeft,
                centerY - half,
                left + size + half,
                centerY + half,
                left + half,
                centerY - half,
                left + half,
                centerY + half);
        });
    }

    private static SafeGdiObjectHandle CreateBrush(uint color)
    {
        nint handle = GdiNativeMethods.CreateSolidBrush(color);
        if (handle == nint.Zero)
        {
            throw new Win32Exception();
        }

        return SafeGdiObjectHandle.FromOwnedHandle(handle);
    }

    private static SafeGdiObjectHandle CreatePen(int width, uint color)
    {
        nint handle = GdiNativeMethods.CreatePen(GdiNativeMethods.PenStyleSolid, width, color);
        if (handle == nint.Zero)
        {
            throw new Win32Exception();
        }

        return SafeGdiObjectHandle.FromOwnedHandle(handle);
    }

    private static SafeMemoryDeviceContextHandle CreateMemoryDeviceContext(nint deviceContext)
    {
        nint handle = GdiNativeMethods.CreateCompatibleDeviceContext(deviceContext);
        if (handle == nint.Zero)
        {
            throw new Win32Exception("Unable to create a GDI memory device context.");
        }

        return SafeMemoryDeviceContextHandle.FromOwnedHandle(handle);
    }

    private static SafeGdiObjectHandle CreateCompatibleBitmap(
        nint deviceContext,
        int width,
        int height)
    {
        nint handle = GdiNativeMethods.CreateCompatibleBitmap(deviceContext, width, height);
        if (handle == nint.Zero)
        {
            throw new Win32Exception("Unable to create the GDI back-buffer bitmap.");
        }

        return SafeGdiObjectHandle.FromOwnedHandle(handle);
    }

    private static bool IsValidSelectedObject(nint handle) =>
        handle != nint.Zero && handle != new nint(-1);

    private static void WithSelectedObjects(
        nint deviceContext,
        SafeGdiObjectHandle brush,
        SafeGdiObjectHandle pen,
        Action draw)
    {
        nint previousBrush = GdiNativeMethods.SelectObject(deviceContext, brush.DangerousGetHandle());
        if (!IsValidSelectedObject(previousBrush))
        {
            throw new Win32Exception("Unable to select the GDI brush.");
        }

        nint previousPen = GdiNativeMethods.SelectObject(deviceContext, pen.DangerousGetHandle());
        if (!IsValidSelectedObject(previousPen))
        {
            _ = GdiNativeMethods.SelectObject(deviceContext, previousBrush);
            throw new Win32Exception("Unable to select the GDI pen.");
        }

        try
        {
            draw();
        }
        finally
        {
            _ = GdiNativeMethods.SelectObject(deviceContext, previousPen);
            _ = GdiNativeMethods.SelectObject(deviceContext, previousBrush);
        }
    }
}
