using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Netapi32
{
    public const uint FILTER_NORMAL_ACCOUNT = 0x2;
    public const uint MAX_PREFERRED_LENGTH = uint.MaxValue;
    public const uint UF_ACCOUNTDISABLE = 0x2;
    public const int NERR_Success = 0;
    public const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_20
    {
        public char* Name;
        public char* FullName;
        public char* Comment;
        public uint Flags;
        public uint UserId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_23
    {
        public char* Name;
        public char* FullName;
        public char* Comment;
        public uint Flags;
        public nint Sid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_24
    {
        public int InternetIdentity;
        public uint Flags;
        public char* InternetProviderName;
        public char* InternetPrincipalName;
        public nint Sid;
    }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserEnum(string? server, uint level, uint filter, out nint buffer, uint maxLength,
        out uint entriesRead, out uint totalEntries, ref uint resumeHandle);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserGetInfo(string? server, string userName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll")]
    public static partial int NetApiBufferFree(nint buffer);
}
