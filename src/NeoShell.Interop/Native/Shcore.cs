using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class Shcore
{
    public const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
