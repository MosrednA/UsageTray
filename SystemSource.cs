using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UsageTray;

/// <summary>Physical RAM and commit charge of this machine, shown as an extra flyout section.</summary>
static class SystemSource
{
    static readonly string Name = L.T("System", "Systeem");

    public static ProviderStatus Read()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return new(Name, [], Error: new Win32Exception().Message);

        return new(Name,
        [
            Row("ram", m.TotalPhys - m.AvailPhys, m.TotalPhys),
            Row("commit", m.TotalPageFile - m.AvailPageFile, m.TotalPageFile),
        ], Note: L.T("click for processes", "klik voor processen"));
    }

    static UsageWindow Row(string label, ulong used, ulong total)
    {
        const double GB = 1 << 30;
        return new(label, total == 0 ? 0 : 100.0 * used / total, TimeSpan.Zero, null, $"{used / GB:0}/{total / GB:0}G");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
