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

    public const uint ABE_RIGHT = 2;
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

    [LibraryImport("shell32.dll")]
    public static partial int SHGetDesktopFolder(out nint folder);

    [LibraryImport("shell32.dll")]
    public static partial int SHCreateItemFromIDList(
        nint idList, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out Com.IShellItem item);

    [LibraryImport("shell32.dll")]
    public static partial uint ILGetSize(nint idList);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetIDListFromObject([MarshalAs(UnmanagedType.Interface)] Com.IShellItem item, out nint idList);

    public const uint SIID_LINK = 29;
    public const uint SIID_WARNING = 78;
    public const uint SIID_INFO = 79;
    public const uint SIID_ERROR = 80;
    public const uint SHGSI_ICONLOCATION = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public nint hIcon;
        public int iSysImageIndex;
        public int iIcon;
        public fixed char szPath[260];
    }

    [LibraryImport("shell32.dll")]
    public static partial int SHGetStockIconInfo(uint id, uint flags, SHSTOCKICONINFO* info);

    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW")]
    public static partial int SHDefExtractIcon(char* iconFile, int index, uint flags, nint* largeIcon, nint* smallIcon, uint size);

    public const uint CMF_NORMAL = 0x0;
    public const uint CMF_DEFAULTONLY = 0x1;
    public const uint CMF_NODEFAULT = 0x20;
    public const uint CMF_EXTENDEDVERBS = 0x100;
    public const uint CMF_CANRENAME = 0x10;

    public const uint GCS_VERBW = 0x4;

    public const uint CMIC_MASK_FLAG_NO_UI = 0x0000_0400;
    public const uint CMIC_MASK_UNICODE = 0x0000_4000;
    public const uint CMIC_MASK_ASYNCOK = 0x0010_0000;
    public const uint CMIC_MASK_SHIFT_DOWN = 0x1000_0000;
    public const uint CMIC_MASK_PTINVOKE = 0x2000_0000;
    public const uint CMIC_MASK_CONTROL_DOWN = 0x4000_0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        /// <summary>An ANSI verb, or a command offset in the low word.</summary>
        public nint lpVerb;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;
        public nint lpTitle;
        public nint lpVerbW;
        public nint lpParametersW;
        public nint lpDirectoryW;
        public nint lpTitleW;
        public User32.POINT ptInvoke;
    }

    public const uint SEE_MASK_IDLIST = 0x0000_0004;
    public const uint SEE_MASK_NOASYNC = 0x0000_0100;
    public const uint SEE_MASK_FLAG_NO_UI = 0x0000_0400;
    public const uint SEE_MASK_FLAG_LOG_USAGE = 0x0400_0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public char* lpVerb;
        public char* lpFile;
        public char* lpParameters;
        public char* lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public char* lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    // Undocumented, exported by ordinal only; Explorer's Win+R calls it.
    [LibraryImport("shell32.dll", EntryPoint = "#61")]
    public static partial void RunFileDlg(nint owner, nint icon, char* directory, char* title, char* description, uint flags);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellExecuteEx(SHELLEXECUTEINFOW* info);
}
