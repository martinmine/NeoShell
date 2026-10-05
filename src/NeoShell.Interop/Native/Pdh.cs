using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

/// <summary>Performance counters (Performance Data Helper).</summary>
internal static unsafe partial class Pdh
{
    public const uint PDH_FMT_DOUBLE = 0x0000_0200;
    public const uint PDH_FMT_NOCAP100 = 0x0000_8000;

    [StructLayout(LayoutKind.Explicit)]
    public struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    public const uint PDH_MORE_DATA = 0x8000_07D2;
    public const uint PDH_CSTATUS_VALID_DATA = 0;
    public const uint PDH_CSTATUS_NEW_DATA = 1;

    /// <summary>One instance's value of a counter with a wildcard instance (<c>\GPU Engine(*)\…</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public char* szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, uint* type, out PDH_FMT_COUNTERVALUE value);

    /// <summary>Called with no buffer it answers <see cref="PDH_MORE_DATA"/> and the size needed.</summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* items);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);
}
