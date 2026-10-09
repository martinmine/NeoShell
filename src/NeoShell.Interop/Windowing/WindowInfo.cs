using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>A snapshot of what the taskbar needs to know about a top-level window.</summary>
/// <param name="ClassName">The window class, e.g. <c>Notepad</c>.</param>
/// <param name="IsAppWindow"><c>WS_EX_APPWINDOW</c>: asks for a taskbar button even if owned.</param>
/// <param name="AppUserModelId">The window's or its packaged process's AppUserModelID, or null.</param>
/// <param name="ProcessPath">Full path of the process's executable, or null if it can't be read.</param>
public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ClassName,
    bool IsVisible,
    bool IsCloaked,
    bool IsAppWindow,
    bool IsToolWindow,
    bool IsNoActivate,
    nint Owner,
    int ProcessId,
    string? AppUserModelId,
    string? ProcessPath)
{
    // PKEY_AppUserModel_ID
    private static readonly Ole32.PROPERTYKEY s_appUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    public static unsafe WindowInfo Read(nint hwnd)
    {
        var exStyle = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        int processId = TopLevelWindows.GetProcessId(hwnd);
        int cloaked = 0;
        Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, &cloaked, sizeof(int));
        (string? path, string? packageAppId) = ReadProcess(processId);
        char* buffer = stackalloc char[256];
        string className = new(buffer, 0, User32.GetClassName(hwnd, buffer, 256));

        return new WindowInfo(
            hwnd,
            ReadTitle(hwnd),
            className,
            User32.IsWindowVisible(hwnd),
            cloaked != 0,
            (exStyle & User32.WS_EX_APPWINDOW) != 0,
            (exStyle & User32.WS_EX_TOOLWINDOW) != 0,
            (exStyle & User32.WS_EX_NOACTIVATE) != 0,
            User32.GetWindow(hwnd, User32.GW_OWNER),
            processId,
            ReadWindowAppId(hwnd) ?? packageAppId ?? ImplicitAppId(className, path),
            path);
    }

    /// <summary>
    /// File Explorer's windows set no AppUserModelID, but Explorer's taskbar gives them File Explorer's, so they group
    /// with its pin rather than as explorer.exe.
    /// </summary>
    internal static string? ImplicitAppId(string className, string? processPath) =>
        className == "CabinetWClass" && string.Equals(Path.GetFileName(processPath), "explorer.exe", StringComparison.OrdinalIgnoreCase)
            ? "Microsoft.Windows.Explorer"
            : null;

    public static unsafe string ReadTitle(nint hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        char* buffer = stackalloc char[length + 1];
        return new string(buffer, 0, User32.GetWindowText(hwnd, buffer, length + 1));
    }

    /// <summary>
    /// The icon the window shows in its title bar, or null. Sends WM_GETICON, so a hung window costs up to the timeout.
    /// </summary>
    public static IconBitmap? ReadIcon(nint hwnd)
    {
        foreach (nint type in (ReadOnlySpan<nint>)[User32.ICON_BIG, User32.ICON_SMALL2, User32.ICON_SMALL])
        {
            if (User32.SendMessageTimeout(hwnd, User32.WM_GETICON, type, 0, User32.SMTO_ABORTIFHUNG, 100, out nint icon) != 0
                && icon != 0)
            {
                return IconBitmap.FromIcon(icon);
            }
        }

        nint classIcon = User32.GetClassLongPtr(hwnd, User32.GCLP_HICON);
        if (classIcon == 0)
            classIcon = User32.GetClassLongPtr(hwnd, User32.GCLP_HICONSM);
        return classIcon != 0 ? IconBitmap.FromIcon(classIcon) : null;
    }

    private static unsafe string? ReadWindowAppId(nint hwnd)
    {
        Guid iid = typeof(IPropertyStore).GUID;
        if (Shell32.SHGetPropertyStoreForWindow(hwnd, iid, out IPropertyStore store) != 0)
            return null;

        Ole32.PROPERTYKEY key = s_appUserModelIdKey;
        Ole32.PROPVARIANT value = default;
        try
        {
            if (store.GetValue(&key, &value) != 0 || value.vt != Ole32.VT_LPWSTR || value.pointer == 0)
                return null;
            string id = new((char*)value.pointer);
            return id.Length > 0 ? id : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }

    /// <summary>
    /// Path of the executable of the process that owns the window, or null. Unlike <see cref="Read"/> it sends the
    /// window nothing: reading the title of a window of NeoShell's own process sends it WM_GETTEXT, which waits for as
    /// long as its thread is busy.
    /// </summary>
    public static string? ReadProcessPath(nint hwnd) => ReadProcess(TopLevelWindows.GetProcessId(hwnd)).Path;

    internal static unsafe (string? Path, string? PackageAppId) ReadProcess(int processId)
    {
        nint process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process == 0)
            return (null, null);

        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            string? path = Kernel32.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;

            size = 1024;
            string? appId = Kernel32.GetApplicationUserModelId(process, ref size, buffer) == 0 ? new string(buffer) : null;
            return (path, string.IsNullOrEmpty(appId) ? null : appId);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }
}
