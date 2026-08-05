using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Runtime;

namespace QuickPods.Spike.TaskbarHost.Discovery;

[SupportedOSPlatform("windows")]
internal static class TaskbarAutomationDiscovery
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string StartButtonAutomationId = "StartButton";
    private const string WidgetsButtonAutomationId = "WidgetsButton";

    internal static async Task<AutomationTaskbarProbe> DiscoverAsync(
        nint taskbarHandle,
        PixelRect taskbarBounds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await MtaBackgroundWorker.RunAsync(
                () => DiscoverOnMta(taskbarHandle, taskbarBounds),
                Timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return AutomationTaskbarProbe.Failed(TaskbarDiscoveryFaultCode.AutomationTimedOut);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return AutomationTaskbarProbe.Failed(TaskbarDiscoveryFaultCode.AutomationWorkerFailed);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Each UIA element is an untrusted cross-process boundary; one failure must produce an incomplete scan, not terminate it.")]
    private static AutomationTaskbarProbe DiscoverOnMta(nint taskbarHandle, PixelRect taskbarBounds)
    {
        List<AutomationButtonSnapshot> buttons = [];
        HashSet<TaskbarDiscoveryFaultCode> faultCodes = [];
        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(taskbarHandle);
        }
        catch (Exception)
        {
            return AutomationTaskbarProbe.Failed(TaskbarDiscoveryFaultCode.AutomationRootUnavailable);
        }

        if (root is null)
        {
            return AutomationTaskbarProbe.Failed(TaskbarDiscoveryFaultCode.AutomationRootUnavailable);
        }

        AutomationElementCollection candidates;
        try
        {
            PropertyCondition buttonCondition = new(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            candidates = root.FindAll(TreeScope.Descendants, buttonCondition);
        }
        catch (Exception)
        {
            return AutomationTaskbarProbe.Failed(TaskbarDiscoveryFaultCode.AutomationEnumerationFailed);
        }

        for (int index = 0; index < candidates.Count; index++)
        {
            AutomationElement element = candidates[index];
            try
            {
                object isOffscreenValue = element.GetCurrentPropertyValue(
                    AutomationElement.IsOffscreenProperty,
                    true);
                if (!TryReadProperty(isOffscreenValue, out bool isOffscreen))
                {
                    faultCodes.Add(TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable);
                    continue;
                }

                if (isOffscreen)
                {
                    continue;
                }

                string automationId = ReadAutomationId(element);
                bool hasNativeHandle = ReadNativeHandlePresence(element);
                object boundsValue = element.GetCurrentPropertyValue(
                    AutomationElement.BoundingRectangleProperty,
                    true);

                if (!TryReadProperty(boundsValue, out Rect automationBounds))
                {
                    faultCodes.Add(TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable);
                    continue;
                }

                if (!TryConvertRectangle(automationBounds, out PixelRect buttonBounds))
                {
                    faultCodes.Add(TaskbarDiscoveryFaultCode.AutomationButtonBoundsInvalid);
                    continue;
                }

                if (!buttonBounds.Intersects(taskbarBounds))
                {
                    faultCodes.Add(TaskbarDiscoveryFaultCode.AutomationButtonOutsideTaskbar);
                }

                buttons.Add(new AutomationButtonSnapshot(
                    automationId,
                    buttonBounds,
                    hasNativeHandle));
            }
            catch (Exception)
            {
                faultCodes.Add(TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable);
            }
        }

        AddIdentityFaults(buttons, faultCodes);
        return new AutomationTaskbarProbe(
            buttons,
            [.. faultCodes.Select(code => new TaskbarDiscoveryFault(code))]);
    }

    private static void AddIdentityFaults(
        IReadOnlyCollection<AutomationButtonSnapshot> buttons,
        HashSet<TaskbarDiscoveryFaultCode> faultCodes)
    {
        int startCount = buttons.Count(
            button => string.Equals(
                button.AutomationId,
                StartButtonAutomationId,
                StringComparison.Ordinal));
        if (startCount == 0)
        {
            faultCodes.Add(TaskbarDiscoveryFaultCode.StartButtonMissing);
        }
        else if (startCount > 1)
        {
            faultCodes.Add(TaskbarDiscoveryFaultCode.StartButtonDuplicate);
        }

        int widgetsCount = buttons.Count(
            button => string.Equals(
                button.AutomationId,
                WidgetsButtonAutomationId,
                StringComparison.Ordinal));
        if (widgetsCount > 1)
        {
            faultCodes.Add(TaskbarDiscoveryFaultCode.WidgetsButtonDuplicate);
        }
    }

    private static bool TryReadProperty<T>(object value, [NotNullWhen(true)] out T? result)
    {
        if (ReferenceEquals(value, AutomationElement.NotSupported) || value is not T typedValue)
        {
            result = default;
            return false;
        }

        result = typedValue;
        return true;
    }

    private static bool TryConvertRectangle(Rect source, out PixelRect result)
    {
        double left = Math.Floor(source.Left);
        double top = Math.Floor(source.Top);
        double right = Math.Ceiling(source.Right);
        double bottom = Math.Ceiling(source.Bottom);
        if (!IsValidCoordinate(left) ||
            !IsValidCoordinate(top) ||
            !IsValidCoordinate(right) ||
            !IsValidCoordinate(bottom))
        {
            result = default;
            return false;
        }

        result = new PixelRect((int)left, (int)top, (int)right, (int)bottom);
        return result.IsValid;
    }

    private static string ReadAutomationId(AutomationElement element)
    {
        try
        {
            object value = element.GetCurrentPropertyValue(
                AutomationElement.AutomationIdProperty,
                true);
            return TryReadProperty(value, out string? automationId)
                ? automationId ?? string.Empty
                : string.Empty;
        }
        catch (Exception)
        {
            // Empty AutomationId is valid. Required landmarks still fail closed
            // through their explicit identity count below.
            return string.Empty;
        }
    }

    private static bool ReadNativeHandlePresence(AutomationElement element)
    {
        try
        {
            object value = element.GetCurrentPropertyValue(
                AutomationElement.NativeWindowHandleProperty,
                true);
            return TryReadProperty(value, out int nativeHandle) && nativeHandle != 0;
        }
        catch (Exception)
        {
            // NativeWindowHandle is diagnostic-only for XAML-backed buttons and
            // is commonly zero. Its absence cannot make placement unsafe.
            return false;
        }
    }

    private static bool IsValidCoordinate(double value)
    {
        return double.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue;
    }

    private static bool IsAutomationFailure(Exception exception)
    {
        return exception is COMException or
            ElementNotAvailableException or
            InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException or
            NotSupportedException;
    }
}

internal sealed record AutomationTaskbarProbe(
    IReadOnlyList<AutomationButtonSnapshot> Buttons,
    IReadOnlyList<TaskbarDiscoveryFault> Faults)
{
    internal static AutomationTaskbarProbe Failed(TaskbarDiscoveryFaultCode code)
    {
        return new AutomationTaskbarProbe([], [new TaskbarDiscoveryFault(code)]);
    }
}
