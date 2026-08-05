namespace QuickPods.Spike.TaskbarHost.Geometry;

/// <summary>
/// Fail-closed interval operations used by taskbar safe-region calculation.
/// </summary>
internal static class IntervalGeometry
{
    public static bool TryExpand(PixelInterval interval, int padding, out PixelInterval expanded)
    {
        expanded = default;

        if (!interval.IsValid || padding < 0)
        {
            return false;
        }

        long start = (long)interval.Start - padding;
        long end = (long)interval.End + padding;
        if (start < int.MinValue || end > int.MaxValue)
        {
            return false;
        }

        expanded = new PixelInterval((int)start, (int)end);
        return expanded.IsValid;
    }

    public static bool TryMerge(
        IReadOnlyList<PixelInterval>? intervals,
        out IReadOnlyList<PixelInterval> merged)
    {
        merged = [];

        if (intervals is null)
        {
            return false;
        }

        if (intervals.Count == 0)
        {
            return true;
        }

        var sorted = new List<PixelInterval>(intervals.Count);
        foreach (PixelInterval interval in intervals)
        {
            if (!interval.IsValid)
            {
                return false;
            }

            sorted.Add(interval);
        }

        sorted.Sort(static (left, right) =>
        {
            int startComparison = left.Start.CompareTo(right.Start);
            return startComparison != 0 ? startComparison : left.End.CompareTo(right.End);
        });

        var result = new List<PixelInterval>(sorted.Count);
        PixelInterval current = sorted[0];
        for (int index = 1; index < sorted.Count; index++)
        {
            PixelInterval next = sorted[index];
            if (next.Start <= current.End)
            {
                var combined = new PixelInterval(current.Start, Math.Max(current.End, next.End));
                if (!combined.IsValid)
                {
                    return false;
                }

                current = combined;
                continue;
            }

            result.Add(current);
            current = next;
        }

        result.Add(current);
        merged = result;
        return true;
    }

    public static bool TrySubtract(
        PixelInterval source,
        IReadOnlyList<PixelInterval>? obstacles,
        out IReadOnlyList<PixelInterval> gaps)
    {
        gaps = [];

        if (!source.IsValid || obstacles is null)
        {
            return false;
        }

        var clippedObstacles = new List<PixelInterval>(obstacles.Count);
        foreach (PixelInterval obstacle in obstacles)
        {
            if (!obstacle.IsValid)
            {
                return false;
            }

            if (obstacle.TryClipTo(source, out PixelInterval clipped))
            {
                clippedObstacles.Add(clipped);
            }
        }

        if (!TryMerge(clippedObstacles, out IReadOnlyList<PixelInterval> merged))
        {
            return false;
        }

        var result = new List<PixelInterval>(merged.Count + 1);
        int cursor = source.Start;
        foreach (PixelInterval obstacle in merged)
        {
            if (cursor < obstacle.Start)
            {
                result.Add(new PixelInterval(cursor, obstacle.Start));
            }

            cursor = Math.Max(cursor, obstacle.End);
        }

        if (cursor < source.End)
        {
            result.Add(new PixelInterval(cursor, source.End));
        }

        gaps = result;
        return true;
    }

    /// <summary>
    /// Chooses the widest interval; equal-width intervals are resolved leftmost-first.
    /// </summary>
    public static bool TryFindMaximumGap(
        IReadOnlyList<PixelInterval>? gaps,
        out PixelInterval maximumGap)
    {
        maximumGap = default;

        if (gaps is null)
        {
            return false;
        }

        bool found = false;
        foreach (PixelInterval gap in gaps)
        {
            if (!gap.IsValid)
            {
                return false;
            }

            if (!found ||
                gap.Length > maximumGap.Length ||
                (gap.Length == maximumGap.Length && gap.Start < maximumGap.Start))
            {
                maximumGap = gap;
                found = true;
            }
        }

        return found;
    }
}
