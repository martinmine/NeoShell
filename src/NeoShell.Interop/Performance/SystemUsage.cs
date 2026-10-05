using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Performance;

/// <param name="CpuPercent">Processor use as Task Manager shows it, 0 to 100.</param>
/// <param name="GpuPercent">The busiest GPU engine's use, as Task Manager's GPU figure; null without GPU counters.</param>
/// <param name="DiskPercent">How much of the time the disks were busy, 0 to 100.</param>
/// <param name="DriveBusyPercent">The same for each drive, by its name ("C:").</param>
public sealed record UsageSample(
    double CpuPercent,
    double? GpuPercent,
    double DiskPercent,
    IReadOnlyDictionary<string, double> DriveBusyPercent,
    ulong MemoryUsed,
    ulong MemoryTotal);

/// <summary>Processor, GPU, disk and memory use, read through performance counters.</summary>
public sealed unsafe class SystemUsage : IDisposable
{
    private const uint Format = Pdh.PDH_FMT_DOUBLE | Pdh.PDH_FMT_NOCAP100;

    private readonly nint _query;
    private readonly nint _cpu;
    private readonly nint _gpu;
    private readonly nint _disk;
    private readonly nint _drives;

    public SystemUsage()
    {
        if (Pdh.PdhOpenQuery(null, 0, out _query) != 0)
            return;
        // Processor Utility is what Task Manager shows; it goes past 100 while the processor runs above its base speed.
        Pdh.PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", 0, out _cpu);
        Pdh.PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _gpu);
        Pdh.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Idle Time", 0, out _disk);
        Pdh.PdhAddEnglishCounter(_query, @"\LogicalDisk(*)\% Idle Time", 0, out _drives);
        // Rates need two readings: this is the first.
        Pdh.PdhCollectQueryData(_query);
    }

    /// <summary>Use since the previous call (or since construction).</summary>
    public UsageSample Sample()
    {
        if (_query != 0)
            Pdh.PdhCollectQueryData(_query);

        var drives = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, double idle) in ReadAll(_drives))
        {
            // Volumes without a letter ("HarddiskVolume3") and the total are left out.
            if (name.Length == 2 && name[1] == ':')
                drives[name] = Math.Clamp(100 - idle, 0, 100);
        }

        var memory = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)sizeof(Kernel32.MEMORYSTATUSEX) };
        Kernel32.GlobalMemoryStatusEx(ref memory);
        return new UsageSample(
            Math.Clamp(Read(_cpu), 0, 100),
            GpuPercent(ReadAll(_gpu)),
            Math.Clamp(100 - Read(_disk, ifMissing: 100), 0, 100),
            drives,
            memory.ullTotalPhys - memory.ullAvailPhys,
            memory.ullTotalPhys);
    }

    public void Dispose()
    {
        if (_query != 0)
            Pdh.PdhCloseQuery(_query);
    }

    /// <summary>
    /// Task Manager's GPU figure: each engine's use summed over the processes using it, then the busiest engine.
    /// Instances are named "pid_1234_luid_0x…_phys_0_eng_3_engtype_3D", one per process and engine.
    /// </summary>
    internal static double? GpuPercent(IReadOnlyList<(string Instance, double Value)> engines)
    {
        if (engines.Count == 0)
            return null;

        return Math.Clamp(engines
            .GroupBy(engine => engine.Instance.IndexOf("luid_", StringComparison.Ordinal) is var at and >= 0 ? engine.Instance[at..] : engine.Instance)
            .Max(engine => engine.Sum(process => process.Value)), 0, 100);
    }

    private static double Read(nint counter, double ifMissing = 0) =>
        counter != 0 && Pdh.PdhGetFormattedCounterValue(counter, Format, null, out Pdh.PDH_FMT_COUNTERVALUE value) == 0
            ? value.doubleValue
            : ifMissing;

    // Every instance of a counter with a wildcard instance; ones without a value yet (just appeared) are left out.
    private static List<(string Instance, double Value)> ReadAll(nint counter)
    {
        var values = new List<(string, double)>();
        uint size = 0;
        if (counter == 0 || Pdh.PdhGetFormattedCounterArray(counter, Format, ref size, out _, null) != Pdh.PDH_MORE_DATA)
            return values;

        void* buffer = NativeMemory.Alloc(size);
        try
        {
            if (Pdh.PdhGetFormattedCounterArray(counter, Format, ref size, out uint count, buffer) != 0)
                return values;

            var items = (Pdh.PDH_FMT_COUNTERVALUE_ITEM*)buffer;
            for (int i = 0; i < count; i++)
            {
                if (items[i].FmtValue.CStatus is Pdh.PDH_CSTATUS_VALID_DATA or Pdh.PDH_CSTATUS_NEW_DATA)
                    values.Add((new string(items[i].szName), items[i].FmtValue.doubleValue));
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
        return values;
    }
}
