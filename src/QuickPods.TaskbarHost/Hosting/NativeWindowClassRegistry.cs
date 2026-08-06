using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Interop;
using QuickPods.TaskbarHost.Runtime;

namespace QuickPods.TaskbarHost.Hosting;

internal static class NativeWindowClassRegistry
{
    internal const string FloatingViewClassName = "QuickPods.Floating.View";
    internal const string RuntimeNotificationClassName = "QuickPods.Runtime.Notification";

    private static readonly HostNativeMethods.NativeWindowProcedure TaskbarWindowProcedure =
        NativeTaskbarHost.StaticWindowProcedure;
    private static readonly HostNativeMethods.NativeWindowProcedure FloatingWindowProcedure =
        NativeFloatingHost.StaticWindowProcedure;
    private static readonly HostNativeMethods.NativeWindowProcedure RuntimeNotificationProcedure =
        RuntimeNotificationWindow.StaticWindowProcedure;
    private static readonly Lazy<Registration> Registered = new(Register, true);

    internal static Registration GetRegistration() => Registered.Value;

    private static Registration Register()
    {
        nint instance = HostNativeMethods.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw new Win32Exception();
        }

        nint taskbarProcedure = Marshal.GetFunctionPointerForDelegate(TaskbarWindowProcedure);
        nint floatingProcedure = Marshal.GetFunctionPointerForDelegate(FloatingWindowProcedure);
        nint notificationProcedure = Marshal.GetFunctionPointerForDelegate(RuntimeNotificationProcedure);
        RegisterClass(
            Win32TaskbarDiscovery.HostViewClassName,
            instance,
            taskbarProcedure);
        RegisterClass(FloatingViewClassName, instance, floatingProcedure);
        RegisterClass(RuntimeNotificationClassName, instance, notificationProcedure);
        return new(
            Win32TaskbarDiscovery.HostViewClassName,
            FloatingViewClassName,
            RuntimeNotificationClassName,
            instance);
    }

    private static void RegisterClass(string className, nint instance, nint procedure)
    {
        var windowClass = new HostNativeMethods.NativeWindowClass
        {
            Size = (uint)Marshal.SizeOf<HostNativeMethods.NativeWindowClass>(),
            WindowProcedure = procedure,
            Instance = instance,
            ClassName = className,
        };

        if (HostNativeMethods.RegisterClass(ref windowClass) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != HostNativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error);
            }

            var existingClass = new HostNativeMethods.NativeWindowClassInfo
            {
                Size = (uint)Marshal.SizeOf<HostNativeMethods.NativeWindowClassInfo>(),
            };
            if (!HostNativeMethods.GetClassInfo(
                    instance,
                    className,
                    ref existingClass) ||
                existingClass.Instance != instance ||
                existingClass.WindowProcedure != procedure)
            {
                throw new InvalidOperationException(
                    "The trusted taskbar window class name is already registered with different ownership.");
            }
        }
    }

    internal readonly record struct Registration(
        string TaskbarViewClassName,
        string FloatingViewClassName,
        string RuntimeNotificationClassName,
        nint Instance);
}
