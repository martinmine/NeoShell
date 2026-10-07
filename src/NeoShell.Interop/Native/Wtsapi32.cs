using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Wtsapi32
{
    public const nint WTS_CURRENT_SERVER_HANDLE = 0;
    public const uint WTS_CURRENT_SESSION = uint.MaxValue;
    public const int WTSUserName = 5;
    public const int WTSDomainName = 7;

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_SESSION_INFO
    {
        public uint SessionId;
        public char* WinStationName;
        public int State;
    }

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSEnumerateSessions(nint server, uint reserved, uint version, out WTS_SESSION_INFO* sessions, out uint count);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSQuerySessionInformation(nint server, uint sessionId, int infoClass, out char* buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    public static partial void WTSFreeMemory(void* memory);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSDisconnectSession(nint server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
