using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Advapi32
{
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint SE_PRIVILEGE_ENABLED = 0x0002;

    public const uint SHUTDOWN_RESTART = 0x0004;
    public const uint SHUTDOWN_POWEROFF = 0x0008;
    public const uint SHUTDOWN_INSTALL_UPDATES = 0x0040;
    public const uint SHUTDOWN_HYBRID = 0x0200;
    public const uint SHUTDOWN_RESTART_BOOTOPTIONS = 0x0400;
    public const uint SHUTDOWN_ARSO = 0x2000;

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

    public const int TokenIntegrityLevel = 25;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int informationClass, void* information, uint length, out uint returnLength);

    [LibraryImport("advapi32.dll")]
    public static partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    public static partial uint* GetSidSubAuthority(nint sid, uint index);

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

    // Not in the SDK's headers; shutdownux.dll asks them (null token: the caller's) before every restart and shut down.
    // Each returns an NTSTATUS.
    [LibraryImport("advapi32.dll")]
    public static partial int LsaIsUserArsoEnabled(nint token, int* enabled);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaIsUserArsoAllowed(int* allowed);

    public const uint REG_NOTIFY_CHANGE_NAME = 0x1;
    public const uint REG_NOTIFY_CHANGE_LAST_SET = 0x4;
    public const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x1000_0000;

    /// <summary>Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(
        nint key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint filter, nint signal, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    /// <summary>Only the key's last write time is asked for here. Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    public static partial int RegQueryInfoKey(
        nint key, char* className, uint* classLength, nint reserved, uint* subKeys, uint* maxSubKeyLength, uint* maxClassLength,
        uint* values, uint* maxValueNameLength, uint* maxValueLength, uint* securityDescriptor, long* lastWriteTime);

    public const int KEY_READ = 0x20019;
    public const int KEY_SET_VALUE = 0x0002;

    /// <summary>Loads an app's private registry hive (such as a package's settings.dat). Returns a Win32 error code.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegLoadAppKey(string file, out nint key, int access, uint options, uint reserved);

    /// <summary>Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegSetValueEx(nint key, string name, uint reserved, uint type, byte* data, uint size);

    /// <summary>Returns a Win32 error code; 0 is success.</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegQueryValueEx(nint key, string name, nint reserved, out uint type, byte* data, ref uint size);
}
