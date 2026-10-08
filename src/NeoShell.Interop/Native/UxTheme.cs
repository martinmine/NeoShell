using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class UxTheme
{
    // Undocumented, exported by ordinal only: whether uxtheme would shake the window (a top-level app window, not the
    // desktop or the taskbar). Explorer checks a shaken window with it.
    [LibraryImport("uxtheme.dll", EntryPoint = "#86")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsValidShakeWindow(nint hwnd);
}
