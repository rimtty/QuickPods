using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Placement;

/// <summary>
/// Calculates a taskbar child-window placement only from verified physical-pixel geometry.
/// Any incomplete, contradictory, or unrepresentable observation fails closed.
/// </summary>
internal static class SafeRegionCalculator
{
    /// <summary>
    /// Verifies that an already-created host rectangle remains safe in a fresh,
    /// complete observation. The rectangle does not need to match the newly
    /// preferred placement, but it must still satisfy every placement invariant.
    /// </summary>
    public static bool IsExistingPlacementSafe(
        TaskbarLayoutObservation? observation,
        TaskbarPlacementOptions options,
        PixelRect existingBounds)
    {
        if (observation is null ||
            !observation.IsComplete ||
            observation.Orientation != TaskbarOrientation.Horizontal ||
            !options.IsValid ||
            !existingBounds.IsValid ||
            !TryValidateGeometry(observation, out PixelRect startButton) ||
            !TryValidateLandmarkOrder(observation, startButton) ||
            !TryConvertMetrics(observation.Dpi, options, out PlacementMetrics metrics) ||
            existingBounds.Width < metrics.CompactMinimumWidth ||
            existingBounds.Width > metrics.StandardWidth ||
            existingBounds.Height != metrics.Height ||
            !TryCreateVerticalBand(
                observation.TaskbarBounds,
                metrics.Height,
                out PixelInterval verticalBand) ||
            existingBounds.VerticalInterval != verticalBand)
        {
            return false;
        }

        CandidateLaneStatus candidateLaneStatus = TryCreateCandidateLane(
            observation,
            startButton,
            metrics.Margin,
            out PixelInterval candidateLane);
        if (candidateLaneStatus != CandidateLaneStatus.Success ||
            !candidateLane.Contains(existingBounds.HorizontalInterval) ||
            !TryCreateExpandedObstacles(
                observation,
                verticalBand,
                candidateLane,
                metrics.Margin,
                out IReadOnlyList<PixelInterval> expandedObstacles))
        {
            return false;
        }

        return VerifyPlacement(
            existingBounds,
            observation,
            candidateLane,
            candidateLane,
            expandedObstacles);
    }

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

        if (!options.IsValid || !TryValidateGeometry(observation, out PixelRect startButton))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.InvalidGeometry);
        }

        if (!TryValidateLandmarkOrder(observation, startButton))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ContradictoryLandmarks);
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

        CandidateLaneStatus candidateLaneStatus = TryCreateCandidateLane(
            observation,
            startButton,
            metrics.Margin,
            out PixelInterval candidateLane);
        if (candidateLaneStatus == CandidateLaneStatus.Overflow)
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (candidateLaneStatus == CandidateLaneStatus.NoFit)
        {
            return TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth);
        }

        if (!TryCreateExpandedObstacles(
                observation,
                verticalBand,
                candidateLane,
                metrics.Margin,
                out IReadOnlyList<PixelInterval> expandedObstacles))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (!IntervalGeometry.TrySubtract(candidateLane, expandedObstacles, out IReadOnlyList<PixelInterval> gaps))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.InvalidGeometry);
        }

        if (!IntervalGeometry.TryFindMaximumGap(gaps, out PixelInterval maximumGap) ||
            maximumGap.Length < metrics.CompactMinimumWidth)
        {
            return TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth);
        }

        TaskbarStripMode mode = maximumGap.Length >= metrics.StandardWidth
            ? TaskbarStripMode.Standard
            : TaskbarStripMode.Compact;
        int width = mode == TaskbarStripMode.Standard ? metrics.StandardWidth : maximumGap.Length;

        if (!TryCenterWithin(maximumGap, width, out PixelInterval horizontalPlacement) ||
            !TryCreateRectangle(horizontalPlacement, verticalBand, out PixelRect placement))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.ConversionFailed);
        }

        if (!VerifyPlacement(
                placement,
                observation,
                candidateLane,
                maximumGap,
                expandedObstacles))
        {
            return TaskbarPlacementResult.TransientUnknown(PlacementReason.VerificationFailed);
        }

        return TaskbarPlacementResult.Place(placement, mode);
    }

    private static bool TryValidateGeometry(
        TaskbarLayoutObservation observation,
        out PixelRect startButton)
    {
        startButton = default;

        PixelRect taskbar = observation.TaskbarBounds;
        if (!taskbar.IsValid ||
            taskbar.Width <= taskbar.Height ||
            observation.Dpi == 0 ||
            observation.StartButtonBounds is not PixelRect start ||
            !start.IsValid ||
            !taskbar.Contains(start) ||
            observation.Obstacles is null)
        {
            return false;
        }

        if (observation.WidgetsButtonBounds is PixelRect widgets &&
            (!widgets.IsValid || !taskbar.Contains(widgets)))
        {
            return false;
        }

        foreach (PixelRect obstacle in observation.Obstacles)
        {
            if (!obstacle.IsValid)
            {
                return false;
            }
        }

        startButton = start;
        return true;
    }

    private static bool TryValidateLandmarkOrder(
        TaskbarLayoutObservation observation,
        PixelRect startButton)
    {
        return observation.WidgetsButtonBounds is not PixelRect widgets ||
            widgets.Right <= startButton.Left;
    }

    private static bool TryConvertMetrics(
        uint dpi,
        TaskbarPlacementOptions options,
        out PlacementMetrics metrics)
    {
        metrics = default;

        if (!DipPixelConverter.TryDipLengthToPixels(options.StandardWidthDip, dpi, out int standardWidth) ||
            !DipPixelConverter.TryDipLengthToPixels(options.CompactMinimumWidthDip, dpi, out int compactMinimumWidth) ||
            !DipPixelConverter.TryDipLengthToPixels(options.HeightDip, dpi, out int height) ||
            !TryConvertMargin(options.MarginDip, dpi, out int margin) ||
            standardWidth < compactMinimumWidth)
        {
            return false;
        }

        metrics = new PlacementMetrics(standardWidth, compactMinimumWidth, height, margin);
        return true;
    }

    private static bool TryConvertMargin(double marginDip, uint dpi, out int margin)
    {
        if (marginDip == 0d)
        {
            margin = 0;
            return dpi != 0;
        }

        return DipPixelConverter.TryDipLengthToPixels(marginDip, dpi, out margin);
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
        return verticalBand.IsValid && taskbar.VerticalInterval.Contains(verticalBand);
    }

    private static CandidateLaneStatus TryCreateCandidateLane(
        TaskbarLayoutObservation observation,
        PixelRect startButton,
        int margin,
        out PixelInterval candidateLane)
    {
        candidateLane = default;

        int leadingLandmark = observation.WidgetsButtonBounds is PixelRect widgets
            ? widgets.Right
            : observation.TaskbarBounds.Left;
        long start = (long)leadingLandmark + margin;
        long end = (long)startButton.Left - margin;
        if (start < int.MinValue || start > int.MaxValue || end < int.MinValue || end > int.MaxValue)
        {
            return CandidateLaneStatus.Overflow;
        }

        candidateLane = new PixelInterval((int)start, (int)end);
        return candidateLane.IsValid && observation.TaskbarBounds.HorizontalInterval.Contains(candidateLane)
            ? CandidateLaneStatus.Success
            : CandidateLaneStatus.NoFit;
    }

    private static bool TryCreateExpandedObstacles(
        TaskbarLayoutObservation observation,
        PixelInterval verticalBand,
        PixelInterval candidateLane,
        int margin,
        out IReadOnlyList<PixelInterval> expandedObstacles)
    {
        var result = new List<PixelInterval>(observation.Obstacles.Count);
        PixelRect taskbar = observation.TaskbarBounds;
        var placementBand = new PixelRect(taskbar.Left, verticalBand.Start, taskbar.Right, verticalBand.End);

        foreach (PixelRect obstacle in observation.Obstacles)
        {
            if (!obstacle.TryIntersect(placementBand, out PixelRect clippedToBand))
            {
                continue;
            }

            if (!IntervalGeometry.TryExpand(clippedToBand.HorizontalInterval, margin, out PixelInterval expanded))
            {
                expandedObstacles = [];
                return false;
            }

            if (expanded.TryClipTo(candidateLane, out PixelInterval clippedToCandidate))
            {
                result.Add(clippedToCandidate);
            }
        }

        expandedObstacles = result;
        return true;
    }

    private static bool TryCenterWithin(
        PixelInterval container,
        int width,
        out PixelInterval centered)
    {
        centered = default;

        if (!container.IsValid || width <= 0 || width > container.Length)
        {
            return false;
        }

        long start = (long)container.Start + ((long)container.Length - width) / 2;
        long end = start + width;
        if (start < int.MinValue || end > int.MaxValue)
        {
            return false;
        }

        centered = new PixelInterval((int)start, (int)end);
        return centered.IsValid && container.Contains(centered);
    }

    private static bool TryCreateRectangle(
        PixelInterval horizontal,
        PixelInterval vertical,
        out PixelRect rectangle)
    {
        rectangle = default;
        if (!horizontal.IsValid || !vertical.IsValid)
        {
            return false;
        }

        rectangle = new PixelRect(horizontal.Start, vertical.Start, horizontal.End, vertical.End);
        return rectangle.IsValid;
    }

    private static bool VerifyPlacement(
        PixelRect placement,
        TaskbarLayoutObservation observation,
        PixelInterval candidateLane,
        PixelInterval selectedGap,
        IReadOnlyList<PixelInterval> expandedObstacles)
    {
        if (!placement.IsValid ||
            !observation.TaskbarBounds.Contains(placement) ||
            !candidateLane.Contains(placement.HorizontalInterval) ||
            !selectedGap.Contains(placement.HorizontalInterval))
        {
            return false;
        }

        foreach (PixelInterval expandedObstacle in expandedObstacles)
        {
            if (placement.HorizontalInterval.Intersects(expandedObstacle))
            {
                return false;
            }
        }

        if (observation.StartButtonBounds is PixelRect startButton &&
            placement.IntersectionArea(startButton) != 0)
        {
            return false;
        }

        if (observation.WidgetsButtonBounds is PixelRect widgetsButton &&
            placement.IntersectionArea(widgetsButton) != 0)
        {
            return false;
        }

        foreach (PixelRect obstacle in observation.Obstacles)
        {
            if (placement.IntersectionArea(obstacle) != 0)
            {
                return false;
            }
        }

        return true;
    }

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
