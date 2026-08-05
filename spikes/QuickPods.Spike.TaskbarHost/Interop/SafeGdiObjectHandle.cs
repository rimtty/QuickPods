using Microsoft.Win32.SafeHandles;

namespace QuickPods.Spike.TaskbarHost.Interop;

internal sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeGdiObjectHandle()
        : base(true)
    {
    }

    internal static SafeGdiObjectHandle FromOwnedHandle(nint handle)
    {
        var safeHandle = new SafeGdiObjectHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    protected override bool ReleaseHandle() => NativeMethods.DeleteObject(handle);
}
