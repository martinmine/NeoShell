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

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, uint* type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);
}
