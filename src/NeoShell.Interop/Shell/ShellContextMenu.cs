using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>
/// The shell's own context menus for desktop items and for the desktop background, as Explorer shows them (shell
/// extensions included), or single commands from them by verb. UI thread only: menus show UI owned by
/// <c>owner</c>, and shell extensions expect an STA.
/// </summary>
public static unsafe class ShellContextMenu
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;
    private const int SW_SHOWNORMAL = 1;

    /// <summary>
    /// Runs a canonical verb (<c>open</c>, <c>runas</c>, <c>openas</c>, <c>cut</c>, <c>copy</c>, <c>delete</c>,
    /// <c>link</c>, <c>copyaspath</c>, <c>properties</c>) on the items.
    /// </summary>
    /// <returns>Whether the command ran; false when the items don't have the verb.</returns>
    public static bool InvokeVerb(nint owner, IReadOnlyList<DesktopItem> items, string verb) =>
        ForItems(owner, items, menu => Invoke(menu, owner, verb));

    /// <summary>Runs the items' default command: what a double-click does.</summary>
    public static bool InvokeDefault(nint owner, IReadOnlyList<DesktopItem> items) =>
        ForItems(owner, items, menu => WithMenu(menu, Shell32.CMF_DEFAULTONLY, handle =>
        {
            uint command = User32.GetMenuDefaultItem(handle, 0, 0);
            return command != uint.MaxValue && Invoke(menu, owner, command, point: null);
        }));

    /// <summary>Runs a verb of the desktop background's menu: <c>paste</c> or <c>pastelink</c>.</summary>
    public static bool InvokeBackgroundVerb(nint owner, string verb) =>
        BackgroundMenu(owner) is { } menu && Invoke(menu, owner, verb);

    /// <summary>
    /// Shows the shell's context menu for the items at a screen point (physical pixels) and runs the chosen command,
    /// except Rename, which only the caller can do (it needs the item's view).
    /// </summary>
    /// <returns>True when the user chose Rename.</returns>
    public static bool Show(nint owner, IReadOnlyList<DesktopItem> items, int x, int y) =>
        ForItems(owner, items, menu => Track(menu, owner, x, y, Shell32.CMF_CANRENAME) == "rename");

    /// <summary>Shows the shell's context menu for the desktop background and runs the chosen command.</summary>
    public static void ShowBackground(nint owner, int x, int y)
    {
        if (BackgroundMenu(owner) is { } menu)
            Track(menu, owner, x, y, Shell32.CMF_NORMAL);
    }

    /// <summary>Asks to empty the Recycle Bin (the shell confirms first).</summary>
    public static void EmptyRecycleBin(nint owner) => Shell32.SHEmptyRecycleBin(owner, null, 0);

    /// <summary>Whether the clipboard holds files to paste.</summary>
    public static bool CanPaste() => User32.IsClipboardFormatAvailable(User32.CF_HDROP);

    private static bool ForItems(nint owner, IReadOnlyList<DesktopItem> items, Func<IContextMenu, bool> use)
    {
        if (items.Count == 0)
            return false;

        IShellFolder desktop = DesktopFolder.Open();
        nint* idLists = stackalloc nint[items.Count];
        for (int i = 0; i < items.Count; i++)
            idLists[i] = items[i].ToNative();
        try
        {
            Guid iid = typeof(IContextMenu).GUID;
            nint menu;
            if (desktop.GetUIObjectOf(owner, (uint)items.Count, idLists, &iid, null, &menu) != 0)
                return false;
            return use(ComPointer.TakeOwnership<IContextMenu>(menu));
        }
        finally
        {
            for (int i = 0; i < items.Count; i++)
                Marshal.FreeCoTaskMem(idLists[i]);
        }
    }

    private static IContextMenu? BackgroundMenu(nint owner)
    {
        IShellFolder desktop = DesktopFolder.Open();
        Guid iid = typeof(IContextMenu).GUID;
        nint menu;
        return desktop.CreateViewObject(owner, &iid, &menu) == 0 ? ComPointer.TakeOwnership<IContextMenu>(menu) : null;
    }

    /// <summary>Fills a temporary menu from the context menu (handlers expect that before a command runs).</summary>
    private static T WithMenu<T>(IContextMenu menu, uint flags, Func<nint, T> use)
    {
        nint handle = User32.CreatePopupMenu();
        try
        {
            if (menu.QueryContextMenu(handle, 0, FirstCommand, LastCommand, flags) < 0)
                return default!;
            return use(handle);
        }
        finally
        {
            User32.DestroyMenu(handle);
        }
    }

    private static bool Invoke(IContextMenu menu, nint owner, string verb) =>
        WithMenu(menu, Shell32.CMF_EXTENDEDVERBS, _ =>
        {
            nint ansiVerb = Marshal.StringToCoTaskMemAnsi(verb);
            try
            {
                fixed (char* wideVerb = verb)
                {
                    var info = NewInvokeInfo(owner, point: null);
                    info.lpVerb = ansiVerb;
                    info.lpVerbW = (nint)wideVerb;
                    return menu.InvokeCommand(&info) == 0;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ansiVerb);
            }
        });

    private static bool Invoke(IContextMenu menu, nint owner, uint command, User32.POINT? point)
    {
        // A command is passed as its offset from the first ID, in place of a verb string.
        var info = NewInvokeInfo(owner, point);
        info.lpVerb = info.lpVerbW = (nint)(command - FirstCommand);
        return menu.InvokeCommand(&info) == 0;
    }

    private static Shell32.CMINVOKECOMMANDINFOEX NewInvokeInfo(nint owner, User32.POINT? point) => new()
    {
        cbSize = (uint)sizeof(Shell32.CMINVOKECOMMANDINFOEX),
        fMask = Shell32.CMIC_MASK_UNICODE
            | (point is null ? 0 : Shell32.CMIC_MASK_PTINVOKE)
            | (KeyboardState.IsShiftDown() ? Shell32.CMIC_MASK_SHIFT_DOWN : 0),
        hwnd = owner,
        nShow = SW_SHOWNORMAL,
        ptInvoke = point ?? default,
    };

    /// <summary>Shows the menu, runs the chosen command and returns its verb (null if none was chosen).</summary>
    private static string? Track(IContextMenu menu, nint owner, int x, int y, uint flags)
    {
        if (KeyboardState.IsShiftDown())
            flags |= Shell32.CMF_EXTENDEDVERBS;

        return WithMenu(menu, flags, handle =>
        {
            // Submenus such as Send to and Open with are filled and drawn on demand, through the owner's messages.
            var menu2 = menu as IContextMenu2;
            var menu3 = menu as IContextMenu3;
            int command;
            using (new WindowSubclass(owner, (message, wParam, lParam) => ForwardMenuMessage(menu2, menu3, message, wParam, lParam)))
            {
                // Without the foreground, the menu wouldn't close when the user clicks elsewhere.
                User32.SetForegroundWindow(owner);
                command = User32.TrackPopupMenuEx(handle, User32.TPM_RETURNCMD | User32.TPM_RIGHTBUTTON, x, y, owner, 0);
            }
            if (command <= 0)
                return null;

            string? verb = GetVerb(menu, (uint)command);
            if (verb != "rename")
                Invoke(menu, owner, (uint)command, new User32.POINT { x = x, y = y });
            return verb;
        });
    }

    private static nint? ForwardMenuMessage(IContextMenu2? menu2, IContextMenu3? menu3, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case User32.WM_INITMENUPOPUP:
            case User32.WM_DRAWITEM:
            case User32.WM_MEASUREITEM:
            case User32.WM_MENUCHAR:
                nint result = 0;
                if (menu3 is not null)
                    return menu3.HandleMenuMsg2(message, wParam, lParam, &result) == 0 ? result : null;
                if (menu2 is not null && message != User32.WM_MENUCHAR)
                    return menu2.HandleMenuMsg(message, wParam, lParam) == 0 ? 0 : null;
                return null;
            default:
                return null;
        }
    }

    private static string? GetVerb(IContextMenu menu, uint command)
    {
        char* verb = stackalloc char[128];
        verb[0] = '\0';
        return menu.GetCommandString(command - FirstCommand, Shell32.GCS_VERBW, null, verb, 128) == 0
            ? new string(verb)
            : null;
    }
}
