using Microsoft.Win32.SafeHandles;

namespace QuickPods.Spike.TaskbarHost.Interop;

internal sealed class SafeMemoryDeviceContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeMemoryDeviceContextHandle()
        : base(true)
    {
    }

    internal static SafeMemoryDeviceContextHandle FromOwnedHandle(nint handle)
    {
        var safeHandle = new SafeMemoryDeviceContextHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    protected override bool ReleaseHandle() => NativeMethods.DeleteDeviceContext(handle);
}
