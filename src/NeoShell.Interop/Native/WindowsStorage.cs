using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class WindowsStorage
{
    // SCNInitializeDesktop, exported by ordinal only: starts the session's change notification server (SHChangeNotify)
    // on a thread of its own in the calling process and registers its window with user32's SetShellChangeNotifyWindow,
    // as Explorer's desktop does through shell32.
    [LibraryImport("windows.storage.dll", EntryPoint = "#1002")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SCNInitializeDesktop();
}
