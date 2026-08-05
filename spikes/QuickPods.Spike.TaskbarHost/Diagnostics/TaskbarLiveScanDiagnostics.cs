using System.Globalization;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Diagnostics;

internal enum TaskbarLiveScanStage
{
    Initial,
    Armed,
    Watchdog,
    Recovery,
}

/// <summary>
/// A bounded diagnostic signature for one live taskbar scan. It deliberately
/// contains no raw HWND, process ID, window name, AutomationId or coordinates.
/// </summary>
internal readonly record struct TaskbarLiveScanSignature(
    bool DiscoveryComplete,
    PlacementDecision Decision,
    PlacementReason Reason,
    string FaultCodes,
    int AutomationButtonCount,
    int CriticalChildCount,
    bool InvalidatedDuringScan,
    bool IgnoredHostMatch)
{
    internal static TaskbarLiveScanSignature Create(
        TaskbarDiscoveryResult discovery,
        TaskbarPlacementResult placement,
        bool invalidatedDuringScan = false)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(placement);

        TaskbarSnapshot? snapshot = discovery.Snapshot;
        string faultCodes = discovery.Faults.Count == 0
            ? "None"
            : string.Join(
                ',',
                discovery.Faults
                    .Select(static fault => fault.Code)
                    .Distinct()
                    .OrderBy(static code => code));
        return new TaskbarLiveScanSignature(
            discovery.IsComplete,
            placement.Decision,
            placement.Reason,
            faultCodes,
            snapshot?.AutomationButtons.Count ?? 0,
            snapshot?.CriticalChildren.Count ?? 0,
            invalidatedDuringScan,
            discovery.IgnoredHostMatch);
    }

    internal TaskbarLiveScanSignature WithInvalidatedDuringScan(bool value) =>
        this with { InvalidatedDuringScan = value };

    internal string Format()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"discovery-complete={FormatBoolean(DiscoveryComplete)}; " +
            $"decision={Decision}; reason={Reason}; faults={FaultCodes}; " +
            $"buttons={AutomationButtonCount}; critical={CriticalChildCount}; " +
            $"invalidated-during-scan={FormatBoolean(InvalidatedDuringScan)}; " +
            $"ignored-host-match={FormatBoolean(IgnoredHostMatch)}");
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";
}

/// <summary>
/// Emits a changed live-scan signature once, then coalesces consecutive equal
/// observations into a count-only summary when the signature changes or the
/// runner exits.
/// </summary>
internal sealed class TaskbarLiveScanDiagnosticCoalescer : IDisposable
{
    private readonly Action<string> writeLine;
    private TaskbarLiveScanStage currentStage;
    private TaskbarLiveScanSignature currentSignature;
    private int suppressedCount;
    private bool hasCurrent;
    private bool disposed;

    internal TaskbarLiveScanDiagnosticCoalescer(Action<string> writeLine)
    {
        this.writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
    }

    internal void Record(TaskbarLiveScanStage stage, TaskbarLiveScanSignature signature)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (hasCurrent && currentStage == stage && currentSignature == signature)
        {
            suppressedCount = checked(suppressedCount + 1);
            return;
        }

        FlushSuppressed();
        currentStage = stage;
        currentSignature = signature;
        suppressedCount = 0;
        hasCurrent = true;
        writeLine($"layout-scan={FormatStage(stage)}; {signature.Format()}");
    }

    internal void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        FlushSuppressed();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        FlushSuppressed();
        disposed = true;
    }

    private void FlushSuppressed()
    {
        if (!hasCurrent || suppressedCount == 0)
        {
            return;
        }

        writeLine(
            $"layout-scan-repeat={suppressedCount}; stage={FormatStage(currentStage)}; " +
            currentSignature.Format());
        suppressedCount = 0;
    }

    private static string FormatStage(TaskbarLiveScanStage stage) => stage switch
    {
        TaskbarLiveScanStage.Initial => "initial",
        TaskbarLiveScanStage.Armed => "armed",
        TaskbarLiveScanStage.Watchdog => "watchdog",
        TaskbarLiveScanStage.Recovery => "recovery",
        _ => throw new InvalidOperationException("Unknown live scan stage."),
    };
}
