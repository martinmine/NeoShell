using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class Wtsapi32
{
    public const nint WTS_CURRENT_SERVER_HANDLE = 0;
    public const uint WTS_CURRENT_SESSION = uint.MaxValue;

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSDisconnectSession(nint server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
