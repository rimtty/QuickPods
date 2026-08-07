using System.ComponentModel;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Interop;
using NativeRect = QuickPods.TaskbarHost.Interop.GdiNativeMethods.NativeRect;

namespace QuickPods.TaskbarHost.Hosting;

internal static class QuickPodsGdiRenderer
{
    internal static void Paint(nint windowHandle, TaskbarStateSnapshot snapshot) =>
        Paint(windowHandle, TaskbarRenderState.FromSnapshot(snapshot), TaskbarRenderTheme.Current);

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
        DrawMetadata(deviceContext, width, height, state, theme);
    }

    private static void DrawMetadata(
        nint deviceContext,
        int width,
        int height,
        TaskbarRenderState state,
        TaskbarRenderTheme theme)
    {
        double scale = height / 40d;
        bool standard = width >= (int)Math.Round(250d * scale);
        int percentLeft = (int)Math.Round((standard ? 137d : 84d) * scale);
        int percentRight = (int)Math.Round((standard ? 174d : 116d) * scale);
        if (percentLeft >= width - 8)
        {
            return;
        }

        DrawLabel(
            deviceContext,
            $"{state.VolumePercent}%",
            new NativeRect
            {
                Left = percentLeft,
                Top = 0,
                Right = Math.Min(width - 4, percentRight),
                Bottom = height,
            },
            Math.Max(10, (int)Math.Round(12d * scale)),
            theme.ForegroundColor,
            GdiNativeMethods.DrawTextCenter);

        int dividerX = (int)Math.Round((standard ? 180d : 121d) * scale);
        int deviceIconLeft = (int)Math.Round((standard ? 190d : 130d) * scale);
        int deviceTextLeft = (int)Math.Round((standard ? 216d : 154d) * scale);
        if (deviceTextLeft >= width - 6)
        {
            return;
        }

        using (SafeGdiObjectHandle dividerPen = CreatePen(
                   Math.Max(1, (int)Math.Round(scale)),
                   theme.SurfaceOutlineColor))
        {
            nint previousPen = GdiNativeMethods.SelectObject(
                deviceContext,
                dividerPen.DangerousGetHandle());
            if (IsValidSelectedObject(previousPen))
            {
                _ = GdiNativeMethods.MoveTo(
                    deviceContext,
                    dividerX,
                    (int)Math.Round(9d * scale),
                    nint.Zero);
                _ = GdiNativeMethods.LineTo(
                    deviceContext,
                    dividerX,
                    height - (int)Math.Round(9d * scale));
                _ = GdiNativeMethods.SelectObject(deviceContext, previousPen);
            }
        }

        DrawHeadphones(
            deviceContext,
            deviceIconLeft,
            height / 2,
            Math.Max(13, (int)Math.Round(18d * scale)),
            state.HasActiveDeviceConnection,
            theme);
        DrawLabel(
            deviceContext,
            state.DeviceLabel,
            new NativeRect
            {
                Left = deviceTextLeft,
                Top = 0,
                Right = width - Math.Max(7, (int)Math.Round(8d * scale)),
                Bottom = height,
            },
            Math.Max(10, (int)Math.Round(12d * scale)),
            theme.ForegroundColor,
            0);
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

    private static void DrawHeadphones(
        nint deviceContext,
        int left,
        int centerY,
        int size,
        bool hasActiveConnection,
        TaskbarRenderTheme theme)
    {
        int top = centerY - (size / 2);
        int bottom = centerY + (size / 2);
        int stroke = Math.Max(1, size / 9);
        using SafeGdiObjectHandle pen = CreatePen(stroke, theme.ForegroundColor);
        using SafeGdiObjectHandle brush = CreateBrush(theme.ForegroundColor);
        WithSelectedObjects(deviceContext, brush, pen, () =>
        {
            _ = GdiNativeMethods.Arc(
                deviceContext,
                left,
                top,
                left + size,
                bottom,
                left + size,
                centerY,
                left,
                centerY);
            int padWidth = Math.Max(3, size / 5);
            _ = GdiNativeMethods.RoundRect(
                deviceContext,
                left - (stroke / 2),
                centerY - 1,
                left + padWidth,
                bottom + 1,
                padWidth,
                padWidth);
            _ = GdiNativeMethods.RoundRect(
                deviceContext,
                left + size - padWidth,
                centerY - 1,
                left + size + (stroke / 2) + 1,
                bottom + 1,
                padWidth,
                padWidth);
        });

        int dotRadius = Math.Max(2, size / 7);
        uint dotColor = hasActiveConnection ? theme.AccentColor : theme.TrackColor;
        using SafeGdiObjectHandle dotBrush = CreateBrush(dotColor);
        using SafeGdiObjectHandle dotPen = CreatePen(1, dotColor);
        WithSelectedObjects(deviceContext, dotBrush, dotPen, () =>
        {
            int dotX = left + size + dotRadius;
            int dotY = bottom - dotRadius;
            _ = GdiNativeMethods.Ellipse(
                deviceContext,
                dotX - dotRadius,
                dotY - dotRadius,
                dotX + dotRadius + 1,
                dotY + dotRadius + 1);
        });
    }

    private static void DrawLabel(
        nint deviceContext,
        string text,
        NativeRect bounds,
        int fontPixelHeight,
        uint color,
        uint horizontalFormat)
    {
        using SafeGdiObjectHandle font = CreateFont(fontPixelHeight);
        nint previousFont = GdiNativeMethods.SelectObject(
            deviceContext,
            font.DangerousGetHandle());
        if (!IsValidSelectedObject(previousFont))
        {
            throw new Win32Exception("Unable to select the taskbar text font.");
        }

        int previousBackgroundMode = GdiNativeMethods.SetBackgroundMode(
            deviceContext,
            GdiNativeMethods.BackgroundModeTransparent);
        uint previousTextColor = GdiNativeMethods.SetTextColor(deviceContext, color);
        try
        {
            _ = GdiNativeMethods.DrawText(
                deviceContext,
                text,
                text.Length,
                ref bounds,
                GdiNativeMethods.DrawTextSingleLine |
                GdiNativeMethods.DrawTextVerticalCenter |
                GdiNativeMethods.DrawTextNoPrefix |
                GdiNativeMethods.DrawTextEndEllipsis |
                horizontalFormat);
        }
        finally
        {
            _ = GdiNativeMethods.SetTextColor(deviceContext, previousTextColor);
            _ = GdiNativeMethods.SetBackgroundMode(deviceContext, previousBackgroundMode);
            _ = GdiNativeMethods.SelectObject(deviceContext, previousFont);
        }
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

    private static SafeGdiObjectHandle CreateFont(int pixelHeight)
    {
        nint handle = GdiNativeMethods.CreateFont(
            -Math.Abs(pixelHeight),
            0,
            0,
            0,
            GdiNativeMethods.FontWeightNormal,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            "Segoe UI");
        if (handle == nint.Zero)
        {
            throw new Win32Exception("Unable to create the taskbar text font.");
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
