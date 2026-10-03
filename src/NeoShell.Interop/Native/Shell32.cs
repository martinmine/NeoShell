using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Shell32
{
    public const uint ABM_NEW = 0x00;
    public const uint ABM_REMOVE = 0x01;
    public const uint ABM_QUERYPOS = 0x02;
    public const uint ABM_SETPOS = 0x03;
    public const uint ABM_ACTIVATE = 0x06;
    public const uint ABM_WINDOWPOSCHANGED = 0x09;

    public const uint ABE_BOTTOM = 3;

    public const nint ABN_POSCHANGED = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public User32.RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    public static partial nuint SHAppBarMessage(uint message, APPBARDATA* data);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetPropertyStoreForWindow(
        nint hwnd, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out Com.IPropertyStore store);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out char* path);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(
        string path, nint bindContext, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out Com.IShellItem item);
}
