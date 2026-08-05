using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint concurrencyModel);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref PropVariant value);
}

internal sealed class ComApartmentScope : IDisposable
{
    private bool _mustUninitialize;

    private ComApartmentScope(bool mustUninitialize)
    {
        _mustUninitialize = mustUninitialize;
    }

    internal static ComApartmentScope EnterMultithreaded()
    {
        int result = NativeMethods.CoInitializeEx(0, NativeConstants.CoInitMultithreaded);
        if (result is NativeConstants.Success or NativeConstants.SuccessAlreadyInitialized)
        {
            return new ComApartmentScope(mustUninitialize: true);
        }

        Marshal.ThrowExceptionForHR(result);
        throw new InvalidOperationException("COM initialization failed without an HRESULT exception.");
    }

    public void Dispose()
    {
        if (!_mustUninitialize)
        {
            return;
        }

        NativeMethods.CoUninitialize();
        _mustUninitialize = false;
        GC.SuppressFinalize(this);
    }
}

internal static class ComObject
{
    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}

internal static class CoTaskMemory
{
    internal static void Free(ref nint pointer)
    {
        if (pointer == 0)
        {
            return;
        }

        Marshal.FreeCoTaskMem(pointer);
        pointer = 0;
    }
}
