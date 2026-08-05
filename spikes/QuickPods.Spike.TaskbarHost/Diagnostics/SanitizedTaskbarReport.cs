using System.Text.Json;
using System.Text.Json.Serialization;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Diagnostics;

internal sealed record SanitizedTaskbarReport(
    int SchemaVersion,
    bool DiscoveryComplete,
    IReadOnlyList<string> Faults,
    SanitizedTaskbarSummary? Taskbar,
    IReadOnlyList<SanitizedLandmark> Landmarks,
    int AutomationButtonCount,
    int CriticalChildCount,
    SanitizedPlacement Placement,
    double DiscoveryElapsedMilliseconds)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SanitizedTaskbarReport Create(
        TaskbarDiscoveryResult discovery,
        TaskbarPlacementResult placement,
        TimeSpan discoveryElapsed)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(placement);

        TaskbarSnapshot? snapshot = discovery.Snapshot;
        SanitizedTaskbarSummary? taskbar = snapshot is null
            ? null
            : new SanitizedTaskbarSummary(
                snapshot.Bounds.Width,
                snapshot.Bounds.Height,
                snapshot.Dpi,
                snapshot.Monitor.IsPrimary);

        IReadOnlyList<SanitizedLandmark> landmarks = snapshot is null
            ? []
            : CreateLandmarks(snapshot);

        SanitizedPlacement sanitizedPlacement = new(
            placement.Decision,
            placement.Reason,
            placement.Mode,
            snapshot is not null && placement.Bounds is PixelRect bounds
                ? ToRelative(bounds, snapshot.Bounds)
                : null);

        return new SanitizedTaskbarReport(
            SchemaVersion: 1,
            DiscoveryComplete: discovery.IsComplete,
            Faults: [.. discovery.Faults
                .Select(static fault => fault.Code.ToString())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
            Taskbar: taskbar,
            Landmarks: landmarks,
            AutomationButtonCount: snapshot?.AutomationButtons.Count ?? 0,
            CriticalChildCount: snapshot?.CriticalChildren.Count ?? 0,
            Placement: sanitizedPlacement,
            DiscoveryElapsedMilliseconds: Math.Round(
                discoveryElapsed.TotalMilliseconds,
                3,
                MidpointRounding.AwayFromZero));
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    private static SanitizedLandmark[] CreateLandmarks(TaskbarSnapshot snapshot)
    {
        return [.. snapshot.AutomationButtons
            .Where(static button => button.AutomationId is "StartButton" or "WidgetsButton")
            .Select(button => new SanitizedLandmark(
                button.AutomationId,
                ToRelative(button.Bounds, snapshot.Bounds)))
            .OrderBy(static landmark => landmark.Kind, StringComparer.Ordinal)];
    }

    private static SanitizedRect ToRelative(PixelRect value, PixelRect origin)
    {
        return new SanitizedRect(
            SubtractChecked(value.Left, origin.Left),
            SubtractChecked(value.Top, origin.Top),
            value.Width,
            value.Height);
    }

    private static int SubtractChecked(int value, int origin)
    {
        long result = (long)value - origin;
        return result is >= int.MinValue and <= int.MaxValue ? (int)result : 0;
    }
}

internal sealed record SanitizedTaskbarSummary(int Width, int Height, uint Dpi, bool IsPrimary);

internal sealed record SanitizedLandmark(string Kind, SanitizedRect Bounds);

internal sealed record SanitizedRect(int X, int Y, int Width, int Height);

internal sealed record SanitizedPlacement(
    PlacementDecision Decision,
    PlacementReason Reason,
    TaskbarStripMode? Mode,
    SanitizedRect? Bounds);
