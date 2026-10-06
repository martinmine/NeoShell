using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Advapi32
{
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint SE_PRIVILEGE_ENABLED = 0x0002;

    public const uint SHUTDOWN_RESTART = 0x0004;
    public const uint SHUTDOWN_POWEROFF = 0x0008;

    // SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED
    public const uint SHTDN_REASON_PLANNED_OTHER = 0x8000_0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(
        nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TOKEN_PRIVILEGES* newState, uint length, nint previous, nint returnLength);

    /// <summary>Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "InitiateShutdownW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint InitiateShutdown(string? machine, string? message, uint gracePeriod, uint flags, uint reason);

    /// <summary>Only the key's last write time is asked for here. Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    public static partial int RegQueryInfoKey(
        nint key, char* className, uint* classLength, nint reserved, uint* subKeys, uint* maxSubKeyLength, uint* maxClassLength,
        uint* values, uint* maxValueNameLength, uint* maxValueLength, uint* securityDescriptor, long* lastWriteTime);
}
