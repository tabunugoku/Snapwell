using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class ProbeReport
{
    internal const int MaxBytes = 64 * 1024;
    internal const string DefaultUrl = "https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json";

    internal static void Before()
    {
        var memory = ReadMemory();
        Console.WriteLine($"before_working_set={memory.WorkingSetSize} before_private={memory.PrivateUsage} " +
            $"before_gc_heap={GC.GetTotalMemory(false)}");
    }

    internal static void After(string probe, uint status, int bytes, double elapsedMs)
    {
        // 測定前に GC を強制せず、通信の直後に残る割り当ても含める。
        var memory = ReadMemory();
        var heap = GC.GetTotalMemory(false);
        Console.WriteLine(FormattableString.Invariant(
            $"probe={probe} status={status} bytes={bytes} elapsed_ms={elapsedMs:F3} working_set={memory.WorkingSetSize} private={memory.PrivateUsage} gc_heap={heap}"));
    }

    internal static Uri ParseUrl(string[] args)
    {
        var uri = new Uri(args.Length > 0 ? args[0] : DefaultUrl, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("HTTP または HTTPS の URL を指定する");
        }
        return uri;
    }

    private static ProcessMemoryCountersEx ReadMemory()
    {
        var counters = new ProcessMemoryCountersEx();
        counters.Cb = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();
        if (!GetProcessMemoryInfo(new nint(-1), ref counters, counters.Cb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessMemoryInfo");
        }
        return counters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        internal uint Cb, PageFaultCount;
        internal nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
        internal nuint QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx counters, uint size);
}
