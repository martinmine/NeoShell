using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>An item of a shell context menu: a command, a submenu or a separator.</summary>
public sealed class ShellMenuItem
{
    internal ShellMenuItem(uint command) => Command = command;

    internal uint Command { get; }

    /// <summary>The label without its access-key marker and shortcut text.</summary>
    public string Text { get; init; } = "";

    /// <summary>The command's canonical verb (<c>open</c>, <c>paste</c>, <c>rename</c>â€¦), when the handler gives one.</summary>
    public string? Verb { get; init; }

    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
    public bool IsDefault { get; init; }
    public IconBitmap? Icon { get; init; }
    public IReadOnlyList<ShellMenuItem> Items { get; init; } = [];
}

/// <summary>
/// The shell's context menu for desktop items or the desktop background, as Explorer builds it (shell extensions
/// and the New, Send to and Open with submenus included), read into <see cref="ShellMenuItem"/>s for NeoShell to show
/// in its own menu. Keep it until the menu closes: the handlers run the chosen command. UI thread only: handlers
/// expect an STA and show UI owned by <c>owner</c>.
/// </summary>
public sealed unsafe class ShellMenu : IDisposable
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    private readonly IContextMenu _menu;
    private readonly nint _owner;
    private readonly nint _handle;

    private ShellMenu(IContextMenu menu, nint owner, uint flags)
    {
        _menu = menu;
        _owner = owner;
        _handle = User32.CreatePopupMenu();
        if (KeyboardState.IsShiftDown())
            flags |= Shell32.CMF_EXTENDEDVERBS;
        Items = menu.QueryContextMenu(_handle, 0, FirstCommand, LastCommand, flags) >= 0 ? Read(_handle) : [];
    }

    public IReadOnlyList<ShellMenuItem> Items { get; }

    /// <summary>The menu for the items; with Rename, which only the caller can do (it needs the item's view).</summary>
    public static ShellMenu? ForItems(nint owner, IReadOnlyList<DesktopItem> items) =>
        ShellContextMenu.CreateForItems(owner, items) is { } menu ? new ShellMenu(menu, owner, Shell32.CMF_CANRENAME) : null;

    /// <summary>The desktop background's menu; like Explorer's, without a default (bold) command.</summary>
    public static ShellMenu? ForBackground(nint owner) =>
        ShellContextMenu.CreateForBackground(owner) is { } menu ? new ShellMenu(menu, owner, Shell32.CMF_NODEFAULT) : null;

    /// <summary>Runs the item's command, as chosen at a screen point (physical pixels).</summary>
    /// <returns>Whether the handler ran it.</returns>
    public bool Invoke(ShellMenuItem item, int x, int y) =>
        ShellContextMenu.Invoke(_menu, _owner, item.Command - FirstCommand, new User32.POINT { x = x, y = y });

    public void Dispose() => User32.DestroyMenu(_handle);

    private List<ShellMenuItem> Read(nint menu)
    {
        var items = new List<ShellMenuItem>();
        int count = User32.GetMenuItemCount(menu);
        for (uint i = 0; i < count; i++)
        {
            var info = new User32.MENUITEMINFOW
            {
                cbSize = (uint)sizeof(User32.MENUITEMINFOW),
                fMask = User32.MIIM_FTYPE | User32.MIIM_STATE | User32.MIIM_ID | User32.MIIM_SUBMENU | User32.MIIM_BITMAP | User32.MIIM_STRING,
            };
            if (!User32.GetMenuItemInfo(menu, i, true, &info))
                continue;

            if ((info.fType & User32.MFT_SEPARATOR) != 0)
            {
                items.Add(new ShellMenuItem(0) { IsSeparator = true });
                continue;
            }

            string text = ReadText(menu, i, info.cch);
            IReadOnlyList<ShellMenuItem> submenu = [];
            if (info.hSubMenu != 0)
            {
                // Submenus like New and Send to are filled when they open; fill them now.
                InitPopup(info.hSubMenu, i);
                submenu = Read(info.hSubMenu);
            }

            items.Add(new ShellMenuItem(info.wID)
            {
                Text = MenuText.Clean(text),
                Verb = info.hSubMenu == 0 ? GetVerb(info.wID) : null,
                IsEnabled = (info.fState & User32.MFS_GRAYED) == 0,
                IsChecked = (info.fState & User32.MFS_CHECKED) != 0,
                IsDefault = (info.fState & User32.MFS_DEFAULT) != 0,
                Icon = IsBitmap(info.hbmpItem) ? IconBitmap.CopyBitmap(info.hbmpItem) : null,
                Items = submenu,
            });
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

    // HBMMENU_CALLBACK (-1) and the small HBMMENU_* values are the system's own menu images, not bitmaps. Real handles
    // can come sign-extended, so compare unsigned.
    private static bool IsBitmap(nint bitmap) => bitmap != -1 && (nuint)bitmap > 11;

    private void InitPopup(nint submenu, uint position)
    {
        nint result = 0;
        if (_menu is IContextMenu3 menu3)
            menu3.HandleMenuMsg2(User32.WM_INITMENUPOPUP, submenu, (nint)position, &result);
        else if (_menu is IContextMenu2 menu2)
            menu2.HandleMenuMsg(User32.WM_INITMENUPOPUP, submenu, (nint)position);
    }

    private string? GetVerb(uint command)
    {
        if (command < FirstCommand || command > LastCommand)
            return null;

        char* verb = stackalloc char[128];
        verb[0] = '\0';
        return _menu.GetCommandString(command - FirstCommand, Shell32.GCS_VERBW, null, verb, 128) == 0 && verb[0] != '\0'
            ? new string(verb)
            : null;
    }
}
