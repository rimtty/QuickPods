using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static partial class ParentProcessVerifier
{
    private const uint SnapshotProcesses = 0x00000002;

    internal static bool IsSameExecutableParent()
    {
        try
        {
            uint currentProcessId = unchecked((uint)Environment.ProcessId);
            uint? parentProcessId = FindParentProcessId(currentProcessId);
            if (parentProcessId is null)
            {
                return false;
            }

            using var parent = Process.GetProcessById(checked((int)parentProcessId.Value));
            string? parentPath = parent.MainModule?.FileName;
            string? currentPath = Environment.ProcessPath;
            return parentPath is not null &&
                currentPath is not null &&
                string.Equals(
                    Path.GetFullPath(parentPath),
                    Path.GetFullPath(currentPath),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            return false;
        }
    }

    private static uint? FindParentProcessId(uint currentProcessId)
    {
        using SafeFileHandle snapshot = NativeMethods.CreateToolhelp32Snapshot(
            SnapshotProcesses,
            0);
        if (snapshot.IsInvalid)
        {
            return null;
        }

        var entry = new ProcessEntry32
        {
            Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
        };
        if (!NativeMethods.Process32First(snapshot, ref entry))
        {
            return null;
        }

        do
        {
            if (entry.ProcessId == currentProcessId)
            {
                return entry.ParentProcessId;
            }
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));

        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry32
    {
        internal uint Size;
        internal uint UsageCount;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint ThreadCount;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        internal fixed char ExecutableFile[260];
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial SafeFileHandle CreateToolhelp32Snapshot(
            uint flags,
            uint processId);

        [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Process32First(
            SafeFileHandle snapshot,
            ref ProcessEntry32 entry);

        [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Process32Next(
            SafeFileHandle snapshot,
            ref ProcessEntry32 entry);
    }
}
