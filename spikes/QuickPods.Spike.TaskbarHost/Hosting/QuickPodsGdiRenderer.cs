using System.ComponentModel;
using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal static class QuickPodsGdiRenderer
{
    private const uint SurfaceColor = 0x0024211F;
    private const uint SurfaceOutlineColor = 0x0045413E;
    private const uint TrackColor = 0x00524D49;
    private const uint AccentColor = 0x00EED95E;
    private const uint ForegroundColor = 0x00F4F2F0;

    internal static void Paint(nint window, double volumeFraction)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out NativePaintStruct paint);
        if (deviceContext == nint.Zero)
        {
            throw new Win32Exception("BeginPaint failed for the QuickPods native host.");
        }

        try
        {
            if (!NativeMethods.GetClientRect(window, out NativeRect client))
            {
                throw new Win32Exception();
            }

            DrawBuffered(deviceContext, client, Math.Clamp(volumeFraction, 0, 1));
        }
        finally
        {
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private static void DrawBuffered(
        nint destinationDeviceContext,
        NativeRect destinationBounds,
        double volumeFraction)
    {
        int width = Math.Max(1, destinationBounds.Right - destinationBounds.Left);
        int height = Math.Max(1, destinationBounds.Bottom - destinationBounds.Top);

        using SafeGdiObjectHandle bitmap = CreateCompatibleBitmap(
            destinationDeviceContext,
            width,
            height);
        using SafeMemoryDeviceContextHandle memoryDeviceContext =
            CreateMemoryDeviceContext(destinationDeviceContext);

        nint previousBitmap = NativeMethods.SelectObject(
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
            Draw(memoryDeviceContext.DangerousGetHandle(), bufferBounds, volumeFraction);

            if (!NativeMethods.BitBlt(
                    destinationDeviceContext,
                    destinationBounds.Left,
                    destinationBounds.Top,
                    width,
                    height,
                    memoryDeviceContext.DangerousGetHandle(),
                    0,
                    0,
                    NativeConstants.RasterOperationSourceCopy))
            {
                throw new Win32Exception("Unable to present the GDI back buffer.");
            }
        }
        finally
        {
            _ = NativeMethods.SelectObject(
                memoryDeviceContext.DangerousGetHandle(),
                previousBitmap);
        }
    }

    private static void Draw(nint deviceContext, NativeRect bounds, double volumeFraction)
    {
        int width = Math.Max(1, bounds.Right - bounds.Left);
        int height = Math.Max(1, bounds.Bottom - bounds.Top);
        int radius = Math.Max(6, height / 3);

        using (SafeGdiObjectHandle transparentBrush = CreateBrush(NativeConstants.TransparentColorKey))
        {
            if (NativeMethods.FillRect(deviceContext, ref bounds, transparentBrush.DangerousGetHandle()) == 0)
            {
                throw new Win32Exception("Unable to clear the layered host to its transparent color key.");
            }
        }

        using SafeGdiObjectHandle surfaceBrush = CreateBrush(SurfaceColor);
        using SafeGdiObjectHandle surfacePen = CreatePen(1, SurfaceOutlineColor);
        WithSelectedObjects(deviceContext, surfaceBrush, surfacePen, () =>
        {
            _ = NativeMethods.RoundRect(
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

        DrawSpeaker(deviceContext, layout.IconLeft, layout.CenterY, layout.IconSize);

        using SafeGdiObjectHandle trackBrush = CreateBrush(TrackColor);
        using SafeGdiObjectHandle trackPen = CreatePen(1, TrackColor);
        WithSelectedObjects(deviceContext, trackBrush, trackPen, () =>
        {
            _ = NativeMethods.RoundRect(
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
        int fillRight = Math.Max(layout.TrackLeft + 1, thumbX);
        using SafeGdiObjectHandle accentBrush = CreateBrush(AccentColor);
        using SafeGdiObjectHandle accentPen = CreatePen(1, AccentColor);
        WithSelectedObjects(deviceContext, accentBrush, accentPen, () =>
        {
            _ = NativeMethods.RoundRect(
                deviceContext,
                layout.TrackLeft,
                layout.TrackTop,
                fillRight,
                layout.TrackBottom,
                layout.TrackHeight,
                layout.TrackHeight);

            int thumbRadius = Math.Clamp(height / 10, 4, 8);
            _ = NativeMethods.Ellipse(
                deviceContext,
                thumbX - thumbRadius,
                layout.CenterY - thumbRadius,
                thumbX + thumbRadius + 1,
                layout.CenterY + thumbRadius + 1);
        });
    }

    private static void DrawSpeaker(nint deviceContext, int left, int centerY, int size)
    {
        int half = Math.Max(4, size / 2);
        NativeGdiPoint[] speaker =
        [
            new(left, centerY - (half / 2)),
            new(left + (half / 2), centerY - (half / 2)),
            new(left + half, centerY - half),
            new(left + half, centerY + half),
            new(left + (half / 2), centerY + (half / 2)),
            new(left, centerY + (half / 2)),
        ];

        using SafeGdiObjectHandle foregroundBrush = CreateBrush(ForegroundColor);
        using SafeGdiObjectHandle foregroundPen = CreatePen(Math.Max(1, size / 8), ForegroundColor);
        WithSelectedObjects(deviceContext, foregroundBrush, foregroundPen, () =>
        {
            _ = NativeMethods.Polygon(deviceContext, speaker, speaker.Length);
            int arcLeft = left + (half / 2);
            _ = NativeMethods.Arc(
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
        nint handle = NativeMethods.CreateSolidBrush(color);
        if (handle == nint.Zero)
        {
            throw new Win32Exception();
        }

        return SafeGdiObjectHandle.FromOwnedHandle(handle);
    }

    private static SafeGdiObjectHandle CreatePen(int width, uint color)
    {
        nint handle = NativeMethods.CreatePen(NativeConstants.PenStyleSolid, width, color);
        if (handle == nint.Zero)
        {
            throw new Win32Exception();
        }

        return SafeGdiObjectHandle.FromOwnedHandle(handle);
    }

    private static SafeMemoryDeviceContextHandle CreateMemoryDeviceContext(nint deviceContext)
    {
        nint handle = NativeMethods.CreateCompatibleDeviceContext(deviceContext);
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
        nint handle = NativeMethods.CreateCompatibleBitmap(deviceContext, width, height);
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
        nint previousBrush = NativeMethods.SelectObject(deviceContext, brush.DangerousGetHandle());
        if (!IsValidSelectedObject(previousBrush))
        {
            throw new Win32Exception("Unable to select the GDI brush.");
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen.DangerousGetHandle());
        if (!IsValidSelectedObject(previousPen))
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
            throw new Win32Exception("Unable to select the GDI pen.");
        }

        try
        {
            draw();
        }
        finally
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }
    }
}
