namespace QuickPods.TaskbarHost.Hosting;

internal readonly record struct SliderLayout(
    int IconLeft,
    int IconSize,
    int CenterY,
    int TrackLeft,
    int TrackTop,
    int TrackRight,
    int TrackBottom)
{
    internal int TrackHeight => TrackBottom - TrackTop;
}

internal static class SliderGeometry
{
    internal static bool TryCreate(int width, int height, out SliderLayout layout)
    {
        layout = default;
        if (width < 2 || height < 2)
        {
            return false;
        }

        int inset = Math.Max(2, height / 12);
        int iconLeft = inset + Math.Max(3, height / 9);
        int iconSize = Math.Clamp(height / 3, 10, 22);
        int centerY = height / 2;
        int proposedLeft = iconLeft + iconSize + Math.Max(8, height / 6);
        int proposedRight = width - inset - Math.Max(8, height / 7);
        int trackLeft = Math.Clamp(proposedLeft, 0, width - 1);
        int trackRight = Math.Clamp(proposedRight, trackLeft + 1, width);
        int trackHeight = Math.Clamp(height / 11, 3, 7);
        int trackTop = Math.Clamp(centerY - (trackHeight / 2), 0, height - 1);
        int trackBottom = Math.Clamp(trackTop + trackHeight, trackTop + 1, height);

        layout = new(
            iconLeft,
            iconSize,
            centerY,
            trackLeft,
            trackTop,
            trackRight,
            trackBottom);
        return true;
    }

    internal static double FractionFromPointerX(SliderLayout layout, int pointerX)
    {
        if (layout.TrackRight <= layout.TrackLeft)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), "The slider track must have positive width.");
        }

        return Math.Clamp(
            (double)(pointerX - layout.TrackLeft) / (layout.TrackRight - layout.TrackLeft),
            0d,
            1d);
    }

    internal static bool ContainsPointer(SliderLayout layout, int pointerX, int pointerY)
    {
        if (layout.TrackRight <= layout.TrackLeft || layout.TrackBottom <= layout.TrackTop)
        {
            return false;
        }

        int verticalPadding = Math.Max(6, layout.IconSize / 2);
        int hitTop = Math.Max(0, layout.TrackTop - verticalPadding);
        int hitBottom = layout.TrackBottom + verticalPadding;
        return pointerX >= layout.TrackLeft &&
            pointerX <= layout.TrackRight &&
            pointerY >= hitTop &&
            pointerY < hitBottom;
    }

    internal static bool ContainsSpeakerPointer(SliderLayout layout, int pointerX, int pointerY)
    {
        if (layout.IconSize <= 0)
        {
            return false;
        }

        int iconTop = layout.CenterY - (layout.IconSize / 2);
        return pointerX >= layout.IconLeft &&
            pointerX < layout.IconLeft + layout.IconSize &&
            pointerY >= iconTop &&
            pointerY < iconTop + layout.IconSize;
    }
}
