using System.Globalization;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Rimlight.Bench;

// Machine and process facts for the reports. WorkingSetBytes is read hundreds of times during a soak, so it must not
// allocate: the harness's own garbage would grow the working set it is measuring. Environment.WorkingSet is fine on
// Windows (GetProcessMemoryInfo into a struct), but on Linux it parses /proc/self/status into strings, so there
// /proc/self/statm is read with pread: from a handle kept open for the process, into a stack buffer per call (so
// concurrent readers, e.g. tests running in parallel, can't tear each other's reads).
internal static class ProcessMemory
{
    private static readonly SafeFileHandle? Statm = OpenStatm();

    /// <summary>The process's working set (resident set) in bytes, without allocating. Safe on any thread.</summary>
    /// <returns>The working set, or 0 if it can't be read.</returns>
    public static long WorkingSetBytes()
    {
        if (Statm is null) return Environment.WorkingSet;
        try
        {
            // statm: "size resident shared text lib data dt", in pages. pread at offset 0 regenerates the file.
            Span<byte> statm = stackalloc byte[256];
            int length = RandomAccess.Read(Statm, statm, 0);
            int i = 0;
            while (i < length && statm[i] != ' ') i++;
            long pages = 0;
            for (i++; i < length && statm[i] is >= (byte)'0' and <= (byte)'9'; i++) pages = pages * 10 + (statm[i] - '0');
            return pages * Environment.SystemPageSize;
        }
        catch (IOException)
        {
            return Environment.WorkingSet;
        }
    }

    /// <summary>The CPU's model name, when the OS reports one.</summary>
    /// <returns>For example "Intel(R) Xeon(R) Processor @ 2.80GHz", or null.</returns>
    public static string? CpuModel()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim();
            }
            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal) && line.IndexOf(':') is int colon and >= 0)
                        return line[(colon + 1)..].Trim();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return null;
    }

    /// <summary>A byte count in MB with one decimal, e.g. "41.3 MB".</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The text.</returns>
    public static string Megabytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0.0} MB");

    /// <summary>A signed byte count in KB, e.g. "+120 KB".</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The text.</returns>
    public static string SignedKilobytes(long bytes)
    {
        long kilobytes = (long)Math.Round(bytes / 1024.0);
        return string.Create(CultureInfo.InvariantCulture, $"{(kilobytes >= 0 ? "+" : "-")}{Math.Abs(kilobytes):N0} KB");
    }

    private static SafeFileHandle? OpenStatm()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            return File.OpenHandle("/proc/self/statm");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
