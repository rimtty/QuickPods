using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using QuickPods.Contracts;

namespace QuickPods.TaskbarObserver;

/// <summary>
/// Owns every Win32 signal source for one verified Explorer generation on a
/// dedicated worker thread: a hidden top-level window that receives shell hook
/// and shell broadcast messages, a WinEvent hook scoped to the Explorer process
/// and the taskbar HWND tree, registry change notifications for taskbar
/// settings, an identity poll, and a settle timer. Nothing here registers a
/// UI Automation client, so browsers and other UIA providers never see the
/// observer as an assistive technology.
/// </summary>
internal sealed class TaskbarSignalSubscription : IDisposable
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private const string ShellHookMessageName = "SHELLHOOK";
    private const string ExplorerAdvancedRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string TaskbarGlomLevelValueName = "TaskbarGlomLevel";
    private const int MaximumTrackedTaskbarWindows = 2048;
    private static readonly string[] WatchedRegistryPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband",
        ExplorerAdvancedRegistryPath,
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3",
        @"Software\Microsoft\Windows\CurrentVersion\Search",
    ];

    private readonly BlockingCollection<ObserverSignal> signals = [];
    private readonly ManualResetEventSlim stop = new(false);
    // Start waits synchronously with a product timeout. Complete inline on the
    // worker so unrelated ThreadPool load cannot cause a false start timeout
    // after the hooks have already been registered.
    private readonly TaskCompletionSource<bool> ready = new();
    private readonly Thread worker;
    private readonly TimeSpan workerTimeout;
    // The hook callback delegate must stay reachable for as long as the hook
    // exists; the field roots it for the lifetime of this subscription.
    private readonly ObserverNativeMethods.WinEventProcedure winEventProcedure;
    private readonly HashSet<nint> taskbarTreeWindows = [];
    private readonly List<RegistryWatch> registryWatches = [];
    private nint[] registryWaitHandles = [];
    private GCHandle instanceHandle;
    private nint windowHandle;
    private nint taskbarWindowHandle;
    private ObserverNativeMethods.PrimaryTaskbarIdentity identity;
    private nint winEventHook;
    private uint taskbarCreatedMessage;
    private uint shellHookMessage;
    private bool shellHookRegistered;
    private bool taskbarLabelsVisible;
    private bool generationRetired;
    private TimeSpan? lastRedrawEmitted;
    private Exception? failure;
    private bool disposed;

    private TaskbarSignalSubscription(TimeSpan workerTimeout)
    {
        this.workerTimeout = workerTimeout;
        winEventProcedure = OnWinEvent;
        worker = new Thread(WorkerEntry)
        {
            IsBackground = true,
            Name = "QuickPods taskbar observer shell signals",
        };
        worker.Start();
    }

    internal bool IsAlive => worker.IsAlive;

    internal Exception? Failure => Volatile.Read(ref failure);

    internal static TaskbarSignalSubscription Start(TimeSpan timeout)
    {
        var subscription = new TaskbarSignalSubscription(timeout);
        try
        {
            subscription.ready.Task.WaitAsync(timeout).GetAwaiter().GetResult();
            return subscription;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    internal bool TryTake(out ObserverSignal signal, TimeSpan timeout) =>
        signals.TryTake(out signal, timeout);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stop.Set();
        nint window = Volatile.Read(ref windowHandle);
        if (window != nint.Zero)
        {
            _ = ObserverNativeMethods.PostMessage(window, ObserverNativeMethods.WmClose, 0, nint.Zero);
        }

        if (worker.Join(workerTimeout))
        {
            signals.Dispose();
            stop.Dispose();
        }
    }

    internal static nint StaticWindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        TaskbarSignalSubscription? target = null;
        try
        {
            if (message == ObserverNativeMethods.WmNcCreate)
            {
                ObserverNativeMethods.NativeCreateStruct creation =
                    Marshal.PtrToStructure<ObserverNativeMethods.NativeCreateStruct>(lParam);
                if (creation.CreateParameters == nint.Zero)
                {
                    return nint.Zero;
                }

                ObserverNativeMethods.SetLastError(0);
                nint previous = ObserverNativeMethods.SetWindowLongPointer(
                    window,
                    ObserverNativeMethods.GwlpUserData,
                    creation.CreateParameters);
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    return nint.Zero;
                }
            }

            nint instancePointer = ObserverNativeMethods.GetWindowLongPointer(
                window,
                ObserverNativeMethods.GwlpUserData);
            if (instancePointer != nint.Zero)
            {
                target = GCHandle.FromIntPtr(instancePointer).Target as TaskbarSignalSubscription;
                if (target is not null)
                {
                    if (message == ObserverNativeMethods.WmNcCreate)
                    {
                        Volatile.Write(ref target.windowHandle, window);
                    }

                    nint result = target.WindowProcedure(window, message, wParam, lParam);
                    if (message == ObserverNativeMethods.WmNcDestroy)
                    {
                        _ = ObserverNativeMethods.SetWindowLongPointer(
                            window,
                            ObserverNativeMethods.GwlpUserData,
                            nint.Zero);
                        if (target.windowHandle == window)
                        {
                            Volatile.Write(ref target.windowHandle, nint.Zero);
                        }
                    }

                    return result;
                }
            }
        }
        catch (Exception exception)
        {
            target?.RecordFailure(exception);
        }

        return ObserverNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }

    private void WorkerEntry()
    {
        try
        {
            identity = ObserverNativeMethods.FindPrimaryTaskbar();
            taskbarWindowHandle = identity.WindowHandle;
            taskbarCreatedMessage = ObserverNativeMethods.RegisterWindowMessage(TaskbarCreatedMessageName);
            shellHookMessage = ObserverNativeMethods.RegisterWindowMessage(ShellHookMessageName);
            if (taskbarCreatedMessage == 0 || shellHookMessage == 0)
            {
                throw new Win32Exception();
            }

            ObserverSignalWindowClass.Registration registration = ObserverSignalWindowClass.GetRegistration();
            instanceHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            nint window = ObserverNativeMethods.CreateWindow(
                ObserverNativeMethods.WindowExtendedStyleToolWindow |
                ObserverNativeMethods.WindowExtendedStyleNoActivate,
                registration.ClassName,
                "QuickPods taskbar signal window",
                ObserverNativeMethods.WindowStylePopup,
                0,
                0,
                0,
                0,
                nint.Zero,
                nint.Zero,
                registration.Instance,
                GCHandle.ToIntPtr(instanceHandle));
            if (window == nint.Zero)
            {
                throw new Win32Exception();
            }

            ThrowIfFailed();
            if (ObserverNativeMethods.GetParent(window) != nint.Zero)
            {
                throw new InvalidOperationException("The observer signal window must remain top-level.");
            }

            if (stop.IsSet)
            {
                return;
            }

            if (!ObserverNativeMethods.RegisterShellHookWindow(window))
            {
                throw new Win32Exception();
            }

            shellHookRegistered = true;
            winEventHook = ObserverNativeMethods.SetWinEventHook(
                ObserverNativeMethods.EventObjectCreate,
                ObserverNativeMethods.EventObjectLocationChange,
                nint.Zero,
                winEventProcedure,
                identity.ExplorerProcessId,
                0,
                ObserverNativeMethods.WinEventOutOfContext | ObserverNativeMethods.WinEventSkipOwnProcess);
            if (winEventHook == nint.Zero)
            {
                throw new Win32Exception();
            }

            SeedTaskbarTree();
            if (ObserverNativeMethods.SetTimer(
                    window,
                    ObserverNativeMethods.IdentityTimerId,
                    (uint)TaskbarSignalPolicy.IdentityPollInterval.TotalMilliseconds,
                    nint.Zero) == 0)
            {
                throw new Win32Exception();
            }

            ArmRegistryWatches();
            RefreshTaskbarSettings();
            ready.TrySetResult(true);
            RunMessageLoop();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref failure, exception);
            ready.TrySetException(exception);
        }
        finally
        {
            Cleanup();
        }
    }

    private void RunMessageLoop()
    {
        while (true)
        {
            nint[] handles = registryWaitHandles;
            uint result = ObserverNativeMethods.MessageWaitForMultipleObjects(
                (uint)handles.Length,
                handles,
                ObserverNativeMethods.Infinite,
                ObserverNativeMethods.QueueStatusAllInput,
                ObserverNativeMethods.MessageWaitInputAvailable);
            if (result == ObserverNativeMethods.WaitFailed)
            {
                throw new Win32Exception();
            }

            if (result < handles.Length)
            {
                OnRegistryChanged((int)result);
                ThrowIfFailed();
                continue;
            }

            while (ObserverNativeMethods.PeekMessage(
                       out ObserverNativeMethods.NativeMessage message,
                       nint.Zero,
                       0,
                       0,
                       ObserverNativeMethods.PeekMessageRemove))
            {
                if (message.Message == ObserverNativeMethods.WmQuit)
                {
                    return;
                }

                _ = ObserverNativeMethods.TranslateMessage(ref message);
                _ = ObserverNativeMethods.DispatchMessage(ref message);
            }

            ThrowIfFailed();
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (window != windowHandle)
        {
            return ObserverNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }

        if (message == shellHookMessage && shellHookMessage != 0)
        {
            OnShellHook((ulong)wParam);
            return nint.Zero;
        }

        switch (message)
        {
            case ObserverNativeMethods.WmTimer:
                OnTimer(wParam);
                return nint.Zero;
            case ObserverNativeMethods.WmClose:
                _ = ObserverNativeMethods.DestroyWindow(window);
                return nint.Zero;
            case ObserverNativeMethods.WmDestroy:
                ObserverNativeMethods.PostQuitMessage(0);
                return nint.Zero;
            default:
                SignalDisposition disposition =
                    TaskbarSignalPolicy.ClassifyWindowMessage(message, taskbarCreatedMessage);
                if (!disposition.IsNone)
                {
                    Apply(disposition);
                    return nint.Zero;
                }

                return ObserverNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }
    }

    private void OnShellHook(ulong code)
    {
        SignalDisposition disposition = TaskbarSignalPolicy.ClassifyShellHook(code, taskbarLabelsVisible);
        if (disposition.IsNone)
        {
            return;
        }

        if (TaskbarSignalPolicy.IsThrottledShellHook(code))
        {
            TimeSpan now = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (!TaskbarSignalPolicy.ShouldEmitThrottled(
                    now,
                    lastRedrawEmitted,
                    TaskbarSignalPolicy.RedrawThrottle,
                    out bool scheduleTrailing))
            {
                if (scheduleTrailing)
                {
                    ArmSettle();
                }

                return;
            }

            lastRedrawEmitted = now;
        }

        Apply(disposition);
    }

    private void OnTimer(nuint timerId)
    {
        if (timerId == ObserverNativeMethods.SettleTimerId)
        {
            _ = ObserverNativeMethods.KillTimer(windowHandle, ObserverNativeMethods.SettleTimerId);
            Apply(TaskbarSignalPolicy.Settle);
            return;
        }

        if (timerId != ObserverNativeMethods.IdentityTimerId || generationRetired)
        {
            return;
        }

        bool changed;
        try
        {
            changed = ObserverNativeMethods.FindPrimaryTaskbar() != identity;
        }
        catch (InvalidOperationException)
        {
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        generationRetired = true;
        _ = ObserverNativeMethods.KillTimer(windowHandle, ObserverNativeMethods.IdentityTimerId);
        Enqueue(new(
            ObserverInvalidationKind.ExplorerGenerationChanged,
            ObserverSourceClassification.External));
        ObserverNativeMethods.PostQuitMessage(0);
    }

    private void OnWinEvent(
        nint hook,
        uint eventId,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        try
        {
            if (window == nint.Zero ||
                objectId != ObserverNativeMethods.ObjectIdWindow ||
                childId != ObserverNativeMethods.ChildIdSelf)
            {
                return;
            }

            bool isInTaskbarTree;
            if (eventId == ObserverNativeMethods.EventObjectDestroy)
            {
                // The HWND is already gone when an out-of-context DESTROY arrives,
                // so ancestry cannot be queried; rely on the tracked tree instead.
                isInTaskbarTree = taskbarTreeWindows.Remove(window);
            }
            else
            {
                isInTaskbarTree = window == taskbarWindowHandle ||
                    ObserverNativeMethods.GetAncestor(window, ObserverNativeMethods.GetAncestorRoot) ==
                    taskbarWindowHandle;
                if (isInTaskbarTree)
                {
                    TrackTaskbarWindow(window);
                }
            }

            Apply(TaskbarSignalPolicy.ClassifyWinEvent(eventId, objectId, childId, isInTaskbarTree));
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private void OnRegistryChanged(int index)
    {
        if (index < 0 || index >= registryWatches.Count)
        {
            return;
        }

        RefreshTaskbarSettings();
        Apply(TaskbarSignalPolicy.ClassifyRegistryChange());
        RegistryWatch watch = registryWatches[index];
        if (!watch.TryArm())
        {
            registryWatches.RemoveAt(index);
            watch.Dispose();
            RebuildRegistryWaitHandles();
        }
    }

    private void Apply(SignalDisposition disposition)
    {
        if (disposition.Kind is { } kind)
        {
            Enqueue(new(kind, disposition.Source));
        }

        if (disposition.ArmSettle)
        {
            ArmSettle();
        }
    }

    private void ArmSettle()
    {
        nint window = windowHandle;
        if (window == nint.Zero)
        {
            return;
        }

        // Re-arming an existing timer id restarts it, so bursts collapse into one
        // settle signal after the last raw signal. Failure is tolerated because
        // the host watchdog remains the last resort.
        _ = ObserverNativeMethods.SetTimer(
            window,
            ObserverNativeMethods.SettleTimerId,
            (uint)TaskbarSignalPolicy.SettleDelay.TotalMilliseconds,
            nint.Zero);
    }

    private void SeedTaskbarTree()
    {
        taskbarTreeWindows.Clear();
        taskbarTreeWindows.Add(taskbarWindowHandle);
        _ = ObserverNativeMethods.EnumChildWindows(
            taskbarWindowHandle,
            (child, _) =>
            {
                TrackTaskbarWindow(child);
                return true;
            },
            nint.Zero);
    }

    private void TrackTaskbarWindow(nint window)
    {
        if (taskbarTreeWindows.Count >= MaximumTrackedTaskbarWindows)
        {
            taskbarTreeWindows.Clear();
            taskbarTreeWindows.Add(taskbarWindowHandle);
        }

        taskbarTreeWindows.Add(window);
    }

    private void ArmRegistryWatches()
    {
        foreach (string path in WatchedRegistryPaths)
        {
            RegistryWatch? watch = RegistryWatch.TryOpen(path);
            if (watch is null)
            {
                continue;
            }

            if (watch.TryArm())
            {
                registryWatches.Add(watch);
            }
            else
            {
                watch.Dispose();
            }
        }

        RebuildRegistryWaitHandles();
    }

    private void RebuildRegistryWaitHandles() =>
        registryWaitHandles = [.. registryWatches.Select(watch => watch.WaitHandle)];

    private void RefreshTaskbarSettings()
    {
        object? glomLevel = null;
        try
        {
            using RegistryKey? advanced = Registry.CurrentUser.OpenSubKey(
                ExplorerAdvancedRegistryPath,
                writable: false);
            glomLevel = advanced?.GetValue(TaskbarGlomLevelValueName);
        }
        catch (Exception exception) when (
            exception is IOException or
            SecurityException or
            UnauthorizedAccessException)
        {
            // Unreadable settings fall back to the default combined layout.
        }

        taskbarLabelsVisible = TaskbarSignalPolicy.AreTaskbarLabelsVisible(glomLevel);
    }

    private void Enqueue(ObserverSignal signal)
    {
        try
        {
            signals.Add(signal);
        }
        catch (InvalidOperationException) when (signals.IsAddingCompleted)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref failure, exception, null);

    private void ThrowIfFailed()
    {
        if (Volatile.Read(ref failure) is { } pending)
        {
            throw new InvalidOperationException("The observer signal window procedure failed.", pending);
        }
    }

    private void Cleanup()
    {
        if (winEventHook != nint.Zero)
        {
            _ = ObserverNativeMethods.UnhookWinEvent(winEventHook);
            winEventHook = nint.Zero;
        }

        nint window = windowHandle;
        if (window != nint.Zero && ObserverNativeMethods.IsWindow(window))
        {
            if (shellHookRegistered)
            {
                _ = ObserverNativeMethods.DeregisterShellHookWindow(window);
            }

            _ = ObserverNativeMethods.KillTimer(window, ObserverNativeMethods.IdentityTimerId);
            _ = ObserverNativeMethods.KillTimer(window, ObserverNativeMethods.SettleTimerId);
            _ = ObserverNativeMethods.DestroyWindow(window);
        }

        shellHookRegistered = false;
        Volatile.Write(ref windowHandle, nint.Zero);
        if (instanceHandle.IsAllocated)
        {
            instanceHandle.Free();
        }

        foreach (RegistryWatch watch in registryWatches)
        {
            watch.Dispose();
        }

        registryWatches.Clear();
        registryWaitHandles = [];
    }

    private sealed class RegistryWatch : IDisposable
    {
        private const uint NotifyFilter =
            ObserverNativeMethods.RegistryNotifyChangeName |
            ObserverNativeMethods.RegistryNotifyChangeLastSet |
            ObserverNativeMethods.RegistryNotifyThreadAgnostic;

        private readonly RegistryKey key;
        private readonly AutoResetEvent changed;

        private RegistryWatch(RegistryKey key, AutoResetEvent changed)
        {
            this.key = key;
            this.changed = changed;
        }

        // The AutoResetEvent stays alive for the lifetime of this watch, so the
        // raw handle remains valid while it sits in the wait array.
        internal nint WaitHandle => changed.SafeWaitHandle.DangerousGetHandle();

        internal static RegistryWatch? TryOpen(string path)
        {
            RegistryKey? key = null;
            try
            {
                key = Registry.CurrentUser.OpenSubKey(path, writable: false);
                if (key is null)
                {
                    return null;
                }

                return new RegistryWatch(key, new AutoResetEvent(false));
            }
            catch (Exception exception) when (
                exception is IOException or
                SecurityException or
                UnauthorizedAccessException)
            {
                key?.Dispose();
                return null;
            }
        }

        internal bool TryArm()
        {
            try
            {
                return ObserverNativeMethods.RegistryNotifyChangeKeyValue(
                    key.Handle,
                    true,
                    NotifyFilter,
                    changed.SafeWaitHandle,
                    true) == 0;
            }
            catch (Exception exception) when (
                exception is IOException or
                ObjectDisposedException or
                UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            // Closing the key cancels any pending notification before the event
            // handle is released.
            key.Dispose();
            changed.Dispose();
        }
    }
}
