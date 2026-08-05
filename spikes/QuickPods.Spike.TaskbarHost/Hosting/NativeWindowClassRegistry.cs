using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal static class NativeWindowClassRegistry
{
    internal const string ViewClassName = "QuickPods.Spike.TaskbarHost.NativeView.v1";
    private const string ControlClassName = "QuickPods.Spike.TaskbarHost.Control.v1";
    private const string FloatingViewClassName = "QuickPods.Spike.FloatingStrip.NativeView.v1";

    private static readonly NativeWindowProcedure WindowProcedure = NativeTaskbarHost.StaticWindowProcedure;
    private static readonly NativeWindowProcedure FloatingWindowProcedure =
        NativeFloatingStripHost.StaticWindowProcedure;
    private static readonly Lazy<Registration> Registered = new(RegisterClasses, true);
    private static readonly Lazy<FloatingRegistration> FloatingRegistered =
        new(RegisterFloatingClass, true);

    internal static Registration GetRegistration() => Registered.Value;

    internal static FloatingRegistration GetFloatingRegistration() => FloatingRegistered.Value;

    private static Registration RegisterClasses()
    {
        nint instance = NativeMethods.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw new Win32Exception();
        }

        nint procedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure);
        RegisterClass(ViewClassName, instance, procedure);
        RegisterClass(ControlClassName, instance, procedure);
        return new(ViewClassName, ControlClassName, instance);
    }

    private static FloatingRegistration RegisterFloatingClass()
    {
        nint instance = NativeMethods.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw new Win32Exception();
        }

        nint procedure = Marshal.GetFunctionPointerForDelegate(FloatingWindowProcedure);
        RegisterClass(FloatingViewClassName, instance, procedure);
        return new(FloatingViewClassName, instance);
    }

    private static void RegisterClass(string className, nint instance, nint procedure)
    {
        var windowClass = new NativeWindowClass
        {
            Size = (uint)Marshal.SizeOf<NativeWindowClass>(),
            WindowProcedure = procedure,
            Instance = instance,
            ClassName = className,
        };

        if (NativeMethods.RegisterClass(ref windowClass) != 0)
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error != NativeConstants.ErrorClassAlreadyExists)
        {
            throw new Win32Exception(error);
        }
    }

    internal readonly record struct Registration(string ViewClassName, string ControlClassName, nint Instance);

    internal readonly record struct FloatingRegistration(string ViewClassName, nint Instance);
}
