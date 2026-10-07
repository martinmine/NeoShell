using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Shlwapi
{
    /// <summary>A memory <c>IStream</c> over a copy of the bytes; 0 on failure.</summary>
    [LibraryImport("shlwapi.dll")]
    public static partial nint SHCreateMemStream(byte* data, uint size);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, char* output, uint length, nint reserved);

    public const int PERCEIVED_TYPE_IMAGE = 2;
    public const int PERCEIVED_TYPE_AUDIO = 3;
    public const int PERCEIVED_TYPE_VIDEO = 4;

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AssocGetPerceivedType(string extension, out int type, out int flags, nint typeName);

    /// <summary>Maps shared memory from <c>SHAllocShared</c>; <paramref name="processId"/> is the process the handle belongs to.</summary>
    [LibraryImport("shlwapi.dll")]
    public static partial void* SHLockShared(nint data, uint processId);

    [LibraryImport("shlwapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SHUnlockShared(void* data);

    public const uint OS_FASTUSERSWITCHING = 26;

    [LibraryImport("shlwapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsOS(uint os);
}
