namespace QuickPods.Spike.TaskbarHost.Geometry;

/// <summary>
/// A half-open rectangle in physical pixels: <c>[Left, Right) x [Top, Bottom)</c>.
/// Coordinates may be negative on the Windows virtual desktop.
/// </summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int X => Left;

    public int Y => Top;

    public int Width => IsValid ? (int)((long)Right - Left) : 0;

    public int Height => IsValid ? (int)((long)Bottom - Top) : 0;

    public bool IsValid =>
        Left < Right &&
        Top < Bottom &&
        (long)Right - Left <= int.MaxValue &&
        (long)Bottom - Top <= int.MaxValue;

    public PixelInterval HorizontalInterval => IsValid ? new PixelInterval(Left, Right) : default;

    public PixelInterval VerticalInterval => IsValid ? new PixelInterval(Top, Bottom) : default;

    public static bool TryCreateFromPositionAndSize(
        int x,
        int y,
        int width,
        int height,
        out PixelRect rectangle)
    {
        rectangle = default;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        long right = (long)x + width;
        long bottom = (long)y + height;
        if (right > int.MaxValue || bottom > int.MaxValue)
        {
            return false;
        }

        rectangle = new PixelRect(x, y, (int)right, (int)bottom);
        return rectangle.IsValid;
    }

    public bool Contains(PixelRect other) =>
        IsValid &&
        other.IsValid &&
        Left <= other.Left &&
        Top <= other.Top &&
        other.Right <= Right &&
        other.Bottom <= Bottom;

    public bool Intersects(PixelRect other) =>
        IsValid &&
        other.IsValid &&
        Left < other.Right &&
        other.Left < Right &&
        Top < other.Bottom &&
        other.Top < Bottom;

    public bool TryIntersect(PixelRect other, out PixelRect intersection)
    {
        intersection = default;

        if (!Intersects(other))
        {
            return false;
        }

        intersection = new PixelRect(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));

        return intersection.IsValid;
    }

    public long IntersectionArea(PixelRect other)
    {
        return TryIntersect(other, out PixelRect intersection)
            ? (long)intersection.Width * intersection.Height
            : 0;
    }
}
