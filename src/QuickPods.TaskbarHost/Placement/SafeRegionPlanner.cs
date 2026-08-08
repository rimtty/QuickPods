using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Placement;

/// <summary>
/// Plans a taskbar strip only from complete, verified physical-pixel geometry.
/// Invalid or contradictory evidence fails closed.
/// </summary>
internal static class SafeRegionPlanner
{
    public static TaskbarPlacementResult Calculate(
        TaskbarLayoutObservation? observation,
        TaskbarPlacementOptions options)
    {
        if (observation is null || !observation.IsComplete)
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation);
        }

        if (observation.Orientation != TaskbarOrientation.Horizontal)
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.UnsupportedOrientation);
        }

        if (!options.IsValid ||
            !TryValidateObservation(
                observation,
                out _,
                out PixelRect notificationArea))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.InvalidGeometry);
        }

        if (!TryConvertMetrics(observation.Dpi, options, out PlacementMetrics metrics))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (metrics.Height > observation.TaskbarBounds.Height)
        {
            return TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientHeight);
        }

        if (!TryCreateVerticalBand(observation.TaskbarBounds, metrics.Height, out PixelInterval verticalBand))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        CandidateLaneStatus candidateStatus = TryCreateCandidateLane(
            observation,
            notificationArea,
            metrics.Margin,
            out PixelInterval candidateLane);
        if (candidateStatus == CandidateLaneStatus.Overflow)
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (candidateStatus == CandidateLaneStatus.NoFit)
        {
            return TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth);
        }

        if (!TryCreateExpandedObstacles(
                observation.Obstacles,
                verticalBand,
                candidateLane,
                metrics.Margin,
                out IReadOnlyList<PixelInterval> expandedObstacles) ||
            !TrySubtract(candidateLane, expandedObstacles, out IReadOnlyList<PixelInterval> gaps))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (!TryFindRightmostFittingGap(
                gaps,
                metrics.CompactMinimumWidth,
                out PixelInterval selectedGap))
        {
            return TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth);
        }

        TaskbarStripMode mode = selectedGap.Length >= metrics.StandardWidth
            ? TaskbarStripMode.Standard
            : TaskbarStripMode.Compact;
        int width = mode == TaskbarStripMode.Standard ? metrics.StandardWidth : selectedGap.Length;
        long right = selectedGap.End;
        long left = right - width;
        if (left < int.MinValue || right > int.MaxValue)
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        var bounds = new PixelRect((int)left, verticalBand.Start, (int)right, verticalBand.End);
        if (!Verify(bounds, observation, candidateLane, selectedGap, expandedObstacles))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.VerificationFailed);
        }

        return TaskbarPlacementResult.Place(bounds, mode);
    }

    public static bool IsExistingPlacementSafe(
        TaskbarLayoutObservation? observation,
        TaskbarPlacementOptions options,
        PixelRect existingBounds)
    {
        if (!existingBounds.IsValid)
        {
            return false;
        }

        TaskbarPlacementResult desired = Calculate(observation, options);
        return desired.Decision == PlacementDecision.Place &&
            desired.Bounds == existingBounds;
    }

    private static bool TryValidateObservation(
        TaskbarLayoutObservation observation,
        out PixelRect startButton,
        out PixelRect notificationArea)
    {
        startButton = default;
        notificationArea = default;
        if (!observation.TaskbarBounds.IsValid ||
            observation.TaskbarBounds.Width <= observation.TaskbarBounds.Height ||
            observation.Dpi == 0 ||
            observation.StartButtonBounds is not PixelRect start ||
            !start.IsValid ||
            !observation.TaskbarBounds.Contains(start) ||
            observation.NotificationAreaBounds is not PixelRect tray ||
            !tray.IsValid ||
            !observation.TaskbarBounds.Contains(tray) ||
            observation.Obstacles is null)
        {
            return false;
        }

        if (observation.WidgetsButtonBounds is PixelRect widgets &&
            (!widgets.IsValid || !observation.TaskbarBounds.Contains(widgets)))
        {
            return false;
        }

        if (observation.Obstacles.Any(obstacle => !obstacle.IsValid))
        {
            return false;
        }

        startButton = start;
        notificationArea = tray;
        return true;
    }

    private static bool TryConvertMetrics(
        uint dpi,
        TaskbarPlacementOptions options,
        out PlacementMetrics metrics)
    {
        metrics = default;
        if (!DpiPixels.TryConvertLength(options.StandardWidthDip, dpi, out int standardWidth) ||
            !DpiPixels.TryConvertLength(options.CompactMinimumWidthDip, dpi, out int compactWidth) ||
            !DpiPixels.TryConvertLength(options.HeightDip, dpi, out int height) ||
            !DpiPixels.TryConvertNonNegativeLength(options.MarginDip, dpi, out int margin) ||
            standardWidth < compactWidth)
        {
            return false;
        }

        metrics = new PlacementMetrics(standardWidth, compactWidth, height, margin);
        return true;
    }

    private static bool TryCreateVerticalBand(
        PixelRect taskbar,
        int height,
        out PixelInterval verticalBand)
    {
        verticalBand = default;
        long top = (long)taskbar.Top + ((long)taskbar.Height - height) / 2;
        long bottom = top + height;
        if (top < int.MinValue || bottom > int.MaxValue)
        {
            return false;
        }

        verticalBand = new PixelInterval((int)top, (int)bottom);
        return taskbar.Vertical.Contains(verticalBand);
    }

    private static CandidateLaneStatus TryCreateCandidateLane(
        TaskbarLayoutObservation observation,
        PixelRect notificationArea,
        int margin,
        out PixelInterval lane)
    {
        lane = default;
        long start = (long)observation.TaskbarBounds.Left + margin;
        long end = (long)notificationArea.Left - margin;
        if (start < int.MinValue || start > int.MaxValue || end < int.MinValue || end > int.MaxValue)
        {
            return CandidateLaneStatus.Overflow;
        }

        if (start >= end)
        {
            return CandidateLaneStatus.NoFit;
        }

        lane = new PixelInterval((int)start, (int)end);
        return observation.TaskbarBounds.Horizontal.Contains(lane)
            ? CandidateLaneStatus.Success
            : CandidateLaneStatus.Overflow;
    }

    private static bool TryCreateExpandedObstacles(
        IReadOnlyList<PixelRect> obstacles,
        PixelInterval verticalBand,
        PixelInterval lane,
        int margin,
        out IReadOnlyList<PixelInterval> expanded)
    {
        var result = new List<PixelInterval>();
        foreach (PixelRect obstacle in obstacles)
        {
            if (!IntervalsOverlap(obstacle.Vertical, verticalBand))
            {
                continue;
            }

            long start = (long)obstacle.Left - margin;
            long end = (long)obstacle.Right + margin;
            if (start < int.MinValue || end > int.MaxValue)
            {
                expanded = [];
                return false;
            }

            var interval = new PixelInterval((int)start, (int)end);
            if (interval.TryClipTo(lane, out PixelInterval clipped))
            {
                result.Add(clipped);
            }
        }

        result.Sort(static (left, right) =>
        {
            int startComparison = left.Start.CompareTo(right.Start);
            return startComparison != 0 ? startComparison : left.End.CompareTo(right.End);
        });

        var merged = new List<PixelInterval>(result.Count);
        foreach (PixelInterval interval in result)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End)
            {
                merged.Add(interval);
                continue;
            }

            PixelInterval previous = merged[^1];
            merged[^1] = new PixelInterval(previous.Start, Math.Max(previous.End, interval.End));
        }

        expanded = merged;
        return true;
    }

    private static bool TrySubtract(
        PixelInterval source,
        IReadOnlyList<PixelInterval> obstacles,
        out IReadOnlyList<PixelInterval> gaps)
    {
        if (!source.IsValid || obstacles.Any(obstacle => !obstacle.IsValid))
        {
            gaps = [];
            return false;
        }

        var result = new List<PixelInterval>(obstacles.Count + 1);
        int cursor = source.Start;
        foreach (PixelInterval obstacle in obstacles)
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

    private static bool TryFindRightmostFittingGap(
        IReadOnlyList<PixelInterval> gaps,
        int minimumWidth,
        out PixelInterval selected)
    {
        selected = default;
        foreach (PixelInterval gap in gaps)
        {
            if (!gap.IsValid)
            {
                return false;
            }

            if (gap.Length >= minimumWidth)
            {
                selected = gap;
            }
        }

        return selected.IsValid;
    }

    private static bool Verify(
        PixelRect bounds,
        TaskbarLayoutObservation observation,
        PixelInterval lane,
        PixelInterval selectedGap,
        IReadOnlyList<PixelInterval> expandedObstacles)
    {
        return bounds.IsValid &&
            observation.TaskbarBounds.Contains(bounds) &&
            lane.Contains(bounds.Horizontal) &&
            selectedGap.Contains(bounds.Horizontal) &&
            observation.StartButtonBounds is PixelRect start &&
            !bounds.Intersects(start) &&
            (observation.WidgetsButtonBounds is not PixelRect widgets || !bounds.Intersects(widgets)) &&
            observation.NotificationAreaBounds is PixelRect notificationArea &&
            !bounds.Intersects(notificationArea) &&
            observation.Obstacles.All(obstacle => bounds.IntersectionArea(obstacle) == 0) &&
            expandedObstacles.All(obstacle => !IntervalsOverlap(bounds.Horizontal, obstacle));
    }

    private static bool IntervalsOverlap(PixelInterval left, PixelInterval right) =>
        left.IsValid && right.IsValid && left.Start < right.End && right.Start < left.End;

    private readonly record struct PlacementMetrics(
        int StandardWidth,
        int CompactMinimumWidth,
        int Height,
        int Margin);

    private enum CandidateLaneStatus
    {
        Success,
        NoFit,
        Overflow,
    }
}
