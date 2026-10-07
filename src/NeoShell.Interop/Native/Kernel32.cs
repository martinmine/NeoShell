using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Kernel32
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? moduleName);

    public const uint EVENT_MODIFY_STATE = 0x0002;

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    public static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("kernel32.dll", EntryPoint = "OpenEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint OpenEvent(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(nint handle);

    public const uint SYNCHRONIZE = 0x00100000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    /// <summary>Returns 0, or APPMODEL_ERROR_NO_APPLICATION (15703) for a process that isn't a packaged app.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial int GetApplicationUserModelId(nint process, ref uint length, char* id);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public const uint GENERIC_READ = 0x8000_0000;
    public const uint GENERIC_WRITE = 0x4000_0000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(Microsoft.Win32.SafeHandles.SafeFileHandle file, byte* buffer, uint bytesToWrite, out uint bytesWritten, nint overlapped);

    public const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    public static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint memory);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [LibraryImport("kernel32.dll")]
    public static partial nuint VirtualQuery(void* address, MEMORY_BASIC_INFORMATION* buffer, nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize,
        out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(
        string root, char* name, uint nameLength, uint* serialNumber, uint* maxComponentLength, uint* flags,
        char* fileSystemName, uint fileSystemNameLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    public const uint TIME_NOSECONDS = 0x2;
    public const uint LOCALE_SSCRIPTS = 0x6C;

    /// <summary>A null locale is the user's, with their own formats from Region settings.</summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetTimeFormatEx(string? locale, uint flags, in SYSTEMTIME time, string? format, char* buffer, int length);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetLocaleInfoEx(string? locale, uint type, char* data, int length);
}
