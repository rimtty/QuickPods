using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.TaskbarHost.Hosting;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Runtime;

/// <summary>
/// Receives Shell broadcasts that are not delivered to message-only windows.
/// The HWND stays hidden and top-level for the lifetime of TaskbarHost.
/// </summary>
internal sealed class RuntimeNotificationWindow : IDisposable
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";

    private GCHandle instanceHandle;
    private Exception? pendingWindowFailure;
    private nint windowHandle;
    private readonly int ownerThreadId;
    private readonly uint taskbarCreatedMessage;
    private int pendingTaskbarCreatedCount;
    private bool disposed;

    internal RuntimeNotificationWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The runtime notification window requires Windows.");
        }

        ownerThreadId = Environment.CurrentManagedThreadId;
        taskbarCreatedMessage = HostNativeMethods.RegisterWindowMessage(TaskbarCreatedMessageName);
        if (taskbarCreatedMessage == 0)
        {
            throw new Win32Exception();
        }

        NativeWindowClassRegistry.Registration registration =
            NativeWindowClassRegistry.GetRegistration();
        try
        {
            instanceHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            windowHandle = HostNativeMethods.CreateWindow(
                NativeWindowStyles.WindowExtendedStyleToolWindow |
                NativeWindowStyles.WindowExtendedStyleNoActivate,
                registration.RuntimeNotificationClassName,
                "QuickPods taskbar recovery control",
                NativeWindowStyles.WindowStylePopup,
                0,
                0,
                0,
                0,
                nint.Zero,
                nint.Zero,
                registration.Instance,
                GCHandle.ToIntPtr(instanceHandle));
            if (windowHandle == nint.Zero)
            {
                throw new Win32Exception();
            }

            if (HostNativeMethods.GetParent(windowHandle) != nint.Zero ||
                TaskbarNativeMethods.IsWindowVisible(windowHandle))
            {
                throw new InvalidOperationException(
                    "The TaskbarCreated control HWND must remain hidden and top-level.");
            }

            NativeWindowVerifier.VerifyIdentity(
                windowHandle,
                NativeWindowClassRegistry.RuntimeNotificationClassName);
            ThrowIfWindowFailed();
        }
        catch
        {
            DestroyCore();
            throw;
        }
    }

    internal int PumpMessages()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwnerThread();

        int dispatched = 0;
        while (HostNativeMethods.PeekMessage(
            out HostNativeMethods.NativeMessage message,
            nint.Zero,
            0,
            0,
            HostNativeMethods.PeekMessageRemove))
        {
            if (message.Message == HostNativeMethods.WmQuit)
            {
                HostNativeMethods.PostQuitMessage(unchecked((int)message.WParam));
                break;
            }

            _ = HostNativeMethods.TranslateMessage(ref message);
            _ = HostNativeMethods.DispatchMessage(ref message);
            dispatched++;
        }

        ThrowIfWindowFailed();
        return dispatched;
    }

    internal bool TryConsumeTaskbarCreated() =>
        Interlocked.Exchange(ref pendingTaskbarCreatedCount, 0) != 0;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        EnsureOwnerThread();
        DestroyCore();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    internal static nint StaticWindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        RuntimeNotificationWindow? target = null;
        try
        {
            if (message == HostNativeMethods.WmNcCreate)
            {
                HostNativeMethods.NativeCreateStruct creation =
                    Marshal.PtrToStructure<HostNativeMethods.NativeCreateStruct>(lParam);
                if (creation.CreateParameters == nint.Zero)
                {
                    return nint.Zero;
                }

                HostNativeMethods.SetLastError(0);
                nint previous = HostNativeMethods.SetWindowLongPointer(
                    window,
                    HostNativeMethods.GwlpUserData,
                    creation.CreateParameters);
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    return nint.Zero;
                }
            }

            nint instancePointer = HostNativeMethods.GetWindowLongPointer(
                window,
                HostNativeMethods.GwlpUserData);
            if (instancePointer != nint.Zero)
            {
                target = GCHandle.FromIntPtr(instancePointer).Target as RuntimeNotificationWindow;
                if (target is not null)
                {
                    if (message == HostNativeMethods.WmNcCreate)
                    {
                        target.windowHandle = window;
                    }

                    nint result = target.WindowProcedure(window, message, wParam, lParam);
                    if (message == HostNativeMethods.WmNcDestroy)
                    {
                        _ = HostNativeMethods.SetWindowLongPointer(
                            window,
                            HostNativeMethods.GwlpUserData,
                            nint.Zero);
                        if (target.windowHandle == window)
                        {
                            target.windowHandle = nint.Zero;
                        }
                    }

                    return result;
                }
            }
        }
        catch (Exception exception)
        {
            target?.RecordWindowFailure(exception);
        }

        return HostNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (window == windowHandle && message == taskbarCreatedMessage)
        {
            Interlocked.Increment(ref pendingTaskbarCreatedCount);
            return nint.Zero;
        }

        return HostNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }

    private void RecordWindowFailure(Exception exception) =>
        Interlocked.CompareExchange(ref pendingWindowFailure, exception, null);

    private void ThrowIfWindowFailed()
    {
        if (Interlocked.Exchange(ref pendingWindowFailure, null) is { } failure)
        {
            throw new InvalidOperationException(
                "The runtime notification window procedure failed.",
                failure);
        }
    }

    private void DestroyCore()
    {
        if (windowHandle != nint.Zero)
        {
            nint handle = windowHandle;
            if (TaskbarNativeMethods.IsWindow(handle) &&
                !HostNativeMethods.DestroyWindow(handle))
            {
                throw new Win32Exception();
            }

            windowHandle = nint.Zero;
        }

        if (instanceHandle.IsAllocated)
        {
            instanceHandle.Free();
        }

        pendingTaskbarCreatedCount = 0;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
        {
            throw new InvalidOperationException(
                "The runtime notification HWND must be used by its creating thread.");
        }
    }
}
