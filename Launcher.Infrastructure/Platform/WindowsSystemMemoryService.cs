using System.ComponentModel;
using System.Runtime.InteropServices;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Platform;

public sealed class WindowsSystemMemoryService : ISystemMemoryService
{
    public SystemMemorySnapshot GetSnapshot()
    {
        var memoryStatus = new MemoryStatusEx();
        memoryStatus.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref memoryStatus))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new SystemMemorySnapshot(
            checked((long)memoryStatus.TotalPhysical),
            checked((long)memoryStatus.AvailablePhysical));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
