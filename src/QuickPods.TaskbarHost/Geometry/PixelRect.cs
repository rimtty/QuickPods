namespace QuickPods.TaskbarHost.Geometry;

/// <summary>
/// A half-open rectangle in physical pixels. Coordinates may be negative on
/// the Windows virtual desktop.
/// </summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => IsValid ? (int)((long)Right - Left) : 0;

    public int Height => IsValid ? (int)((long)Bottom - Top) : 0;

    public bool IsValid =>
        Left < Right &&
        Top < Bottom &&
        (long)Right - Left <= int.MaxValue &&
        (long)Bottom - Top <= int.MaxValue;

    public PixelInterval Horizontal => IsValid ? new PixelInterval(Left, Right) : default;

    public PixelInterval Vertical => IsValid ? new PixelInterval(Top, Bottom) : default;

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

    public long IntersectionArea(PixelRect other)
    {
        if (!Intersects(other))
        {
            return 0;
        }

        long width = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        long height = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return width * height;
    }

    public static bool TryFromPositionAndSize(
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
}
