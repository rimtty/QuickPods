using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickPods.Windows.Bluetooth.Worker;

internal sealed partial class BluetoothWorkerJob : IDisposable
{
    private const int BasicAccountingInformationClass = 1;
    private const int ExtendedLimitInformationClass = 9;
    private const uint LimitKillOnJobClose = 0x00002000;

    private readonly SafeFileHandle handle;
    private int disposed;

    private BluetoothWorkerJob(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    internal static BluetoothWorkerJob Create()
    {
        SafeFileHandle handle = NativeMethods.CreateJobObject(nint.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = LimitKillOnJobClose,
            },
        };
        if (!NativeMethods.SetInformationJobObject(
            handle,
            ExtendedLimitInformationClass,
            in limits,
            (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return new(handle);
    }

    internal void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!NativeMethods.AssignProcessToJobObject(handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    internal async Task<bool> TerminateAndConfirmEmptyAsync(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!TryGetActiveProcessCount(out uint activeProcesses))
        {
            return false;
        }

        if (activeProcesses != 0 && !NativeMethods.TerminateJobObject(handle, 1))
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        while (activeProcesses != 0 && stopwatch.Elapsed < timeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None).ConfigureAwait(false);
            if (!TryGetActiveProcessCount(out activeProcesses))
            {
                return false;
            }
        }

        return activeProcesses == 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            handle.Dispose();
        }
    }

    private bool TryGetActiveProcessCount(out uint activeProcesses)
    {
        bool success = NativeMethods.QueryInformationJobObject(
            handle,
            BasicAccountingInformationClass,
            out JobObjectBasicAccountingInformation information,
            (uint)Marshal.SizeOf<JobObjectBasicAccountingInformation>(),
            out _);
        activeProcesses = success ? information.ActiveProcesses : 0;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        internal long TotalUserTime;
        internal long TotalKernelUserTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelUserTime;
        internal uint TotalPageFaultCount;
        internal uint TotalProcesses;
        internal uint ActiveProcesses;
        internal uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateJobObject(nint jobAttributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            SafeFileHandle job,
            int informationClass,
            in JobObjectExtendedLimitInformation information,
            uint informationLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(
            SafeFileHandle job,
            SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryInformationJobObject(
            SafeFileHandle job,
            int informationClass,
            out JobObjectBasicAccountingInformation information,
            uint informationLength,
            out uint returnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    }
}
