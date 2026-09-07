using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuickPods.TaskbarObserver;

internal static class ObserverSignalWindowClass
{
    internal const string ClassName = "QuickPods.Observer.Signal";

    // The delegate must outlive every window of this class; a static field keeps
    // the marshalled thunk alive for the process lifetime.
    private static readonly ObserverNativeMethods.NativeWindowProcedure Procedure =
        TaskbarSignalSubscription.StaticWindowProcedure;
    private static readonly Lazy<Registration> Registered = new(Register, true);

    internal static Registration GetRegistration() => Registered.Value;

    private static Registration Register()
    {
        nint instance = ObserverNativeMethods.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw new Win32Exception();
        }

        nint procedure = Marshal.GetFunctionPointerForDelegate(Procedure);
        var windowClass = new ObserverNativeMethods.NativeWindowClass
        {
            Size = (uint)Marshal.SizeOf<ObserverNativeMethods.NativeWindowClass>(),
            WindowProcedure = procedure,
            Instance = instance,
            ClassName = ClassName,
        };

        if (ObserverNativeMethods.RegisterClass(ref windowClass) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ObserverNativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error);
            }

            var existingClass = new ObserverNativeMethods.NativeWindowClassInfo
            {
                Size = (uint)Marshal.SizeOf<ObserverNativeMethods.NativeWindowClassInfo>(),
            };
            if (!ObserverNativeMethods.GetClassInfo(instance, ClassName, ref existingClass) ||
                existingClass.Instance != instance ||
                existingClass.WindowProcedure != procedure)
            {
                throw new InvalidOperationException(
                    "The observer signal window class is already registered with different ownership.");
            }
        }

        return new(ClassName, instance);
    }

    internal readonly record struct Registration(string ClassName, nint Instance);
}
