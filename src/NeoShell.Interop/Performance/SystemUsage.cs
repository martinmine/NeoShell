using NeoShell.Interop.Native;

namespace NeoShell.Interop.Performance;

/// <param name="CpuPercent">Processor use as Task Manager shows it, 0 to 100.</param>
/// <param name="DiskPercent">How much of the time the disks were busy, 0 to 100.</param>
public readonly record struct UsageSample(double CpuPercent, double DiskPercent, ulong MemoryUsed, ulong MemoryTotal);

/// <summary>Processor, disk and memory use, read through performance counters.</summary>
public sealed unsafe class SystemUsage : IDisposable
{
    private readonly nint _query;
    private readonly nint _cpu;
    private readonly nint _disk;

    public SystemUsage()
    {
        if (Pdh.PdhOpenQuery(null, 0, out _query) != 0)
            return;
        // Processor Utility is what Task Manager shows; it goes past 100 while the processor runs above its base speed.
        Pdh.PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", 0, out _cpu);
        Pdh.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Idle Time", 0, out _disk);
        // Rates need two readings: this is the first.
        Pdh.PdhCollectQueryData(_query);
    }

    /// <summary>Use since the previous call (or since construction).</summary>
    public UsageSample Sample()
    {
        if (_query != 0)
            Pdh.PdhCollectQueryData(_query);
        double cpu = Read(_cpu);
        double disk = 100 - Read(_disk, ifMissing: 100);

        var memory = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)sizeof(Kernel32.MEMORYSTATUSEX) };
        Kernel32.GlobalMemoryStatusEx(ref memory);
        return new UsageSample(
            Math.Clamp(cpu, 0, 100),
            Math.Clamp(disk, 0, 100),
            memory.ullTotalPhys - memory.ullAvailPhys,
            memory.ullTotalPhys);
    }

    public void Dispose()
    {
        if (_query != 0)
            Pdh.PdhCloseQuery(_query);
    }

    private static double Read(nint counter, double ifMissing = 0) =>
        counter != 0 && Pdh.PdhGetFormattedCounterValue(counter, Pdh.PDH_FMT_DOUBLE | Pdh.PDH_FMT_NOCAP100, null, out Pdh.PDH_FMT_COUNTERVALUE value) == 0
            ? value.doubleValue
            : ifMissing;
}
