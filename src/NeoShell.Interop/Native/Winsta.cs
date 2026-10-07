using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class Winsta
{
    /// <summary>
    /// Connects the console to another user's session and shows its lock screen. Not in the SDK's headers;
    /// windows.internal.shell.broker.dll calls it to switch to an account that's signed in.
    /// </summary>
    [LibraryImport("winsta.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool WinStationConnectAndLockDesktop(nint server, uint sessionId);
}
