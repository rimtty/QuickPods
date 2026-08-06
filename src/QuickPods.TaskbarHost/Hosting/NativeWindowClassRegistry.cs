using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Hosting;

internal static class NativeWindowClassRegistry
{
    private static readonly HostNativeMethods.NativeWindowProcedure WindowProcedure =
        NativeTaskbarHost.StaticWindowProcedure;
    private static readonly Lazy<Registration> Registered = new(Register, true);

    internal static Registration GetRegistration() => Registered.Value;

    private static Registration Register()
    {
        nint instance = HostNativeMethods.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw new Win32Exception();
        }

        nint procedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure);
        var windowClass = new HostNativeMethods.NativeWindowClass
        {
            Size = (uint)Marshal.SizeOf<HostNativeMethods.NativeWindowClass>(),
            WindowProcedure = procedure,
            Instance = instance,
            ClassName = Win32TaskbarDiscovery.HostViewClassName,
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
                    Win32TaskbarDiscovery.HostViewClassName,
                    ref existingClass) ||
                existingClass.Instance != instance ||
                existingClass.WindowProcedure != procedure)
            {
                throw new InvalidOperationException(
                    "The trusted taskbar window class name is already registered with different ownership.");
            }
        }

        return new(Win32TaskbarDiscovery.HostViewClassName, instance);
    }

    internal readonly record struct Registration(string ViewClassName, nint Instance);
}
