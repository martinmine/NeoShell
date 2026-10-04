using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Single commands from the shell's context menus for desktop items and the desktop background, by verb; the whole
/// menus are <see cref="ShellMenu"/>. UI thread only: commands show UI owned by <c>owner</c>, and shell extensions
/// expect an STA.
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
        CreateForItems(owner, items) is { } menu && Invoke(menu, owner, verb);

    /// <summary>Runs the items' default command: what a double-click does.</summary>
    public static bool InvokeDefault(nint owner, IReadOnlyList<DesktopItem> items) =>
        CreateForItems(owner, items) is { } menu && WithMenu(menu, Shell32.CMF_DEFAULTONLY, handle =>
        {
            uint command = User32.GetMenuDefaultItem(handle, 0, 0);
            return command != uint.MaxValue && Invoke(menu, owner, command - FirstCommand, point: null);
        });

    /// <summary>Runs a verb of the desktop background's menu: <c>paste</c> or <c>pastelink</c>.</summary>
    public static bool InvokeBackgroundVerb(nint owner, string verb) =>
        CreateForBackground(owner) is { } menu && Invoke(menu, owner, verb);

    /// <summary>Whether the clipboard holds files to paste.</summary>
    public static bool CanPaste() => User32.IsClipboardFormatAvailable(User32.CF_HDROP);

    internal static IContextMenu? CreateForItems(nint owner, IReadOnlyList<DesktopItem> items)
    {
        if (items.Count == 0)
            return null;

        IShellFolder desktop = DesktopFolder.Open();
        nint* idLists = stackalloc nint[items.Count];
        for (int i = 0; i < items.Count; i++)
            idLists[i] = items[i].ToNative();
        try
        {
            Guid iid = typeof(IContextMenu).GUID;
            nint menu;
            return desktop.GetUIObjectOf(owner, (uint)items.Count, idLists, &iid, null, &menu) == 0
                ? ComPointer.TakeOwnership<IContextMenu>(menu)
                : null;
        }
        finally
        {
            for (int i = 0; i < items.Count; i++)
                Marshal.FreeCoTaskMem(idLists[i]);
        }
    }

    internal static IContextMenu? CreateForBackground(nint owner)
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

    /// <summary>Runs a command by its offset from the menu's first ID, which is passed in place of a verb string.</summary>
    internal static bool Invoke(IContextMenu menu, nint owner, uint offset, User32.POINT? point)
    {
        var info = NewInvokeInfo(owner, point);
        info.lpVerb = info.lpVerbW = (nint)offset;
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
}
