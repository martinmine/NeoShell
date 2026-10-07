using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Dsreg
{
    /// <summary>
    /// Whether the PC is joined to a domain or Microsoft Entra ID. Not in the SDK's headers; shutdownux.dll calls it
    /// with these arguments. Returns an HRESULT.
    /// </summary>
    [LibraryImport("dsreg.dll")]
    public static partial int DsrIsDeviceJoined(int* joined, nint reserved);
}
