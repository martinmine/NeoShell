using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class BluetoothApis
{
    /// <summary>
    /// Drops a remote device's connection (undocumented: sends the documented IOCTL_BTH_DISCONNECT_DEVICE to the given
    /// radio, or to every radio when it's 0). Returns a Win32 error code; 0 when a radio disconnected it.
    /// </summary>
    [LibraryImport("BluetoothApis.dll")]
    public static partial uint BluetoothDisconnectDevice(nint radio, in ulong address);
}
