namespace QuickPods.Spike.TaskbarHost.Geometry;

/// <summary>
/// A half-open interval in physical pixels: <c>[Start, End)</c>.
/// </summary>
internal readonly record struct PixelInterval(int Start, int End)
{
    public bool IsValid =>
        Start < End &&
        (long)End - Start <= int.MaxValue;

    public int Length => IsValid ? (int)((long)End - Start) : 0;

    public bool Contains(PixelInterval other) =>
        IsValid &&
        other.IsValid &&
        Start <= other.Start &&
        other.End <= End;

    public bool Intersects(PixelInterval other) =>
        IsValid &&
        other.IsValid &&
        Start < other.End &&
        other.Start < End;

    public bool TryClipTo(PixelInterval bounds, out PixelInterval clipped)
    {
        clipped = default;

        if (!IsValid || !bounds.IsValid)
        {
            return false;
        }

        int start = Math.Max(Start, bounds.Start);
        int end = Math.Min(End, bounds.End);
        if (start >= end)
        {
            return false;
        }

        clipped = new PixelInterval(start, end);
        return true;
    }
}
