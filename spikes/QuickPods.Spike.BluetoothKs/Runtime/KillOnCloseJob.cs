using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal sealed partial class KillOnCloseJob : IDisposable
{
    private const int BasicAccountingInformationClass = 1;
    private const int ExtendedLimitInformationClass = 9;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorFileNotFound = 2;
    private const uint JobObjectQuery = 0x0004;
    private const uint JobObjectTerminate = 0x0008;
    private const uint LimitKillOnJobClose = 0x00002000;

    private readonly SafeFileHandle _handle;
    private int _disposed;

    private KillOnCloseJob(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static KillOnCloseJob Create(string? name = null)
    {
        SafeFileHandle handle = NativeMethods.CreateJobObject(nint.Zero, name);
        int createError = Marshal.GetLastPInvokeError();
        if (handle.IsInvalid)
        {
            throw new Win32Exception(createError);
        }

        if (name is not null && createError == ErrorAlreadyExists)
        {
            handle.Dispose();
            throw new InvalidOperationException(
                "The named containment Job already exists; refusing to join it.");
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = LimitKillOnJobClose,
            },
        };
        bool configured = NativeMethods.SetInformationJobObject(
            handle,
            ExtendedLimitInformationClass,
            in limits,
            (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>());
        if (!configured)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return new KillOnCloseJob(handle);
    }

    public static async Task<JobRecoveryStatus> RecoverNamedAsync(
        string name,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        SafeFileHandle handle = NativeMethods.OpenJobObject(
            JobObjectQuery | JobObjectTerminate,
            inheritHandle: false,
            name);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return error == ErrorFileNotFound
                ? JobRecoveryStatus.NoPriorJob
                : JobRecoveryStatus.Unproven;
        }

        using var job = new KillOnCloseJob(handle);
        return await job.TerminateAndConfirmEmptyAsync(timeout).ConfigureAwait(false)
            ? JobRecoveryStatus.Recovered
            : JobRecoveryStatus.Unproven;
    }

    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!NativeMethods.AssignProcessToJobObject(_handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public async Task<bool> TerminateAndConfirmEmptyAsync(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!TryGetActiveProcessCount(out uint activeProcesses))
        {
            return false;
        }

        if (activeProcesses == 0)
        {
            return true;
        }

        if (!NativeMethods.TerminateJobObject(_handle, exitCode: 1))
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (!TryGetActiveProcessCount(out activeProcesses))
            {
                return false;
            }

            if (activeProcesses == 0)
            {
                return true;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None).ConfigureAwait(false);
        }

        return TryGetActiveProcessCount(out activeProcesses) &&
            activeProcesses == 0;
    }

    private bool TryGetActiveProcessCount(out uint activeProcesses)
    {
        bool queried = NativeMethods.QueryInformationJobObject(
            _handle,
            BasicAccountingInformationClass,
            out JobObjectBasicAccountingInformation information,
            (uint)Marshal.SizeOf<JobObjectBasicAccountingInformation>(),
            out _);
        activeProcesses = queried ? information.ActiveProcesses : 0;
        return queried;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _handle.Dispose();
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
        internal long TotalKernelTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelTime;
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
        internal static partial SafeFileHandle CreateJobObject(
            nint jobAttributes,
            string? name);

        [LibraryImport("kernel32.dll", EntryPoint = "OpenJobObjectW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle OpenJobObject(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            string name);

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
        internal static partial bool TerminateJobObject(
            SafeFileHandle job,
            uint exitCode);
    }
}

internal enum JobRecoveryStatus
{
    NoPriorJob,
    Recovered,
    Unproven,
}
