using NeoShell.Interop.Native;
using NeoShell.Interop.Shell;

namespace NeoShell.Interop.Windowing;

/// <summary>The picture Windows draws beside a system menu command.</summary>
public enum SystemMenuGlyph { None, Restore, Minimize, Maximize, Close }

/// <summary>An item of a window's system menu: a command (with its shortcut text), a submenu or a separator.</summary>
public sealed record SystemMenuItem(uint Command, string Text, string Shortcut, bool IsSeparator, bool IsEnabled, bool IsDefault,
    SystemMenuGlyph Glyph, IReadOnlyList<SystemMenuItem> Items);

/// <summary>
/// A window's system menu (Restore, Move, Size, Minimize, Maximize, Close and whatever the app added), read for a menu
/// of NeoShell's own, as XP's taskbar showed it on a right-click of a task button, and its commands sent to the window.
/// </summary>
public static unsafe class SystemMenu
{
    /// <summary>
    /// The window's system menu, with the standard commands enabled as the window's state allows (as Explorer sets them
    /// before showing the menu; Windows only does when the window shows its own).
    /// </summary>
    public static IReadOnlyList<SystemMenuItem> Read(nint hwnd)
    {
        nint menu = User32.GetSystemMenu(hwnd, false);
        if (menu == 0)
            return [];

        uint style = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE);
        var window = new SystemMenuWindow(User32.IsIconic(hwnd), User32.IsZoomed(hwnd), (style & User32.WS_MINIMIZEBOX) != 0,
            (style & User32.WS_MAXIMIZEBOX) != 0, (style & User32.WS_THICKFRAME) != 0);
        return Read(menu, window);
    }

    /// <summary>
    /// Sends the command to the window. Move and Size (and an app's own commands) work with the keyboard in the window,
    /// so it's brought forward first, as Explorer does.
    /// </summary>
    public static void Invoke(nint hwnd, uint command)
    {
        if (command is not ((uint)User32.SC_MINIMIZE or (uint)User32.SC_CLOSE))
            TopLevelWindows.Activate(hwnd);
        User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, (nint)command, 0);
    }

    private static List<SystemMenuItem> Read(nint menu, SystemMenuWindow window)
    {
        var items = new List<SystemMenuItem>();
        int count = User32.GetMenuItemCount(menu);
        for (uint i = 0; i < count; i++)
        {
            var info = new User32.MENUITEMINFOW
            {
                cbSize = (uint)sizeof(User32.MENUITEMINFOW),
                fMask = User32.MIIM_FTYPE | User32.MIIM_STATE | User32.MIIM_ID | User32.MIIM_SUBMENU | User32.MIIM_STRING,
            };
            if (!User32.GetMenuItemInfo(menu, i, true, &info))
                continue;

            if ((info.fType & User32.MFT_SEPARATOR) != 0)
            {
                items.Add(new SystemMenuItem(0, "", "", true, false, false, SystemMenuGlyph.None, []));
                continue;
            }

            string label = ReadText(menu, i, info.cch);
            int tab = label.IndexOf('\t');
            bool enabled = window.IsEnabled(info.wID) ?? (info.fState & User32.MFS_GRAYED) == 0;
            items.Add(new SystemMenuItem(info.wID, MenuText.Clean(label), tab >= 0 ? label[(tab + 1)..] : "", false, enabled,
                window.DefaultCommand is { } command ? info.wID == command : (info.fState & User32.MFS_DEFAULT) != 0,
                SystemMenuWindow.GlyphFor(info.wID), info.hSubMenu != 0 ? Read(info.hSubMenu, window) : []));
        }
        return items;
    }

    private static string ReadText(nint menu, uint position, uint length)
    {
        if (length == 0)
            return "";

        char* text = stackalloc char[(int)length + 1];
        var info = new User32.MENUITEMINFOW
        {
            cbSize = (uint)sizeof(User32.MENUITEMINFOW),
            fMask = User32.MIIM_STRING,
            dwTypeData = text,
            cch = length + 1,
        };
        return User32.GetMenuItemInfo(menu, position, true, &info) ? new string(text, 0, (int)info.cch) : "";
    }
}

/// <summary>
/// Which standard system menu commands a window allows, as XP's taskbar enabled them: Restore when minimized or maximized,
/// Move unless minimized or maximized, Size for a sizable window that's neither, Minimize and Maximize when the window has the box and
/// isn't so already; the default (bold) command is what double-clicking the caption does.
/// </summary>
public sealed record SystemMenuWindow(bool IsMinimized, bool IsMaximized, bool HasMinimizeBox, bool HasMaximizeBox, bool IsSizable)
{
    /// <summary>Whether the command is allowed, or null for a command that isn't one of the standard ones.</summary>
    public bool? IsEnabled(uint command) => (nint)command switch
    {
        User32.SC_RESTORE => IsMinimized || IsMaximized,
        User32.SC_MOVE => !IsMinimized && !IsMaximized,
        User32.SC_SIZE => IsSizable && !IsMinimized && !IsMaximized,
        User32.SC_MINIMIZE => HasMinimizeBox && !IsMinimized,
        User32.SC_MAXIMIZE => HasMaximizeBox && !IsMaximized,
        User32.SC_CLOSE => true,
        _ => null,
    };

    /// <summary>Restore for a minimized or maximized window, Maximize for one that can be, otherwise none.</summary>
    public uint? DefaultCommand => IsMinimized || IsMaximized ? (uint)User32.SC_RESTORE
        : HasMaximizeBox ? (uint)User32.SC_MAXIMIZE
        : null;

    public static SystemMenuGlyph GlyphFor(uint command) => (nint)command switch
    {
        User32.SC_RESTORE => SystemMenuGlyph.Restore,
        User32.SC_MINIMIZE => SystemMenuGlyph.Minimize,
        User32.SC_MAXIMIZE => SystemMenuGlyph.Maximize,
        User32.SC_CLOSE => SystemMenuGlyph.Close,
        _ => SystemMenuGlyph.None,
    };
}
