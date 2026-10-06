using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// An item of the desktop namespace: a file or folder on the user's or the public Desktop, or a system folder such
/// as This PC or the Recycle Bin. A snapshot: names and attributes don't follow later changes.
/// </summary>
public sealed class DesktopItem
{
    internal DesktopItem(byte[] idList, string name, string editName, string parsingName, string? path, uint attributes)
    {
        IdList = idList;
        Name = name;
        EditName = editName;
        ParsingName = parsingName;
        Path = path;
        Attributes = attributes;
    }

    /// <summary>The item's ID list relative to the desktop, which is also an absolute one: the desktop is the root.</summary>
    internal byte[] IdList { get; }

    internal uint Attributes { get; }

    /// <summary>The name Explorer shows.</summary>
    public string Name { get; }

    /// <summary>The name to edit when renaming (without a hidden extension).</summary>
    public string EditName { get; }

    /// <summary>A file system path, or <c>::{CLSID}</c> for a system folder. Identifies the item across refreshes.</summary>
    public string ParsingName { get; }

    /// <summary>The file system path, or null for a system folder.</summary>
    public string? Path { get; }

    /// <summary>A folder you can open, not a file the shell also browses into (such as a .zip).</summary>
    public bool IsFolder => Has(DesktopFolder.SFGAO_FOLDER) && !Has(DesktopFolder.SFGAO_STREAM);

    public bool IsLink => Has(DesktopFolder.SFGAO_LINK);

    public bool CanCopy => Has(DesktopFolder.SFGAO_CANCOPY);

    public bool CanMove => Has(DesktopFolder.SFGAO_CANMOVE);

    public bool CanRename => Has(DesktopFolder.SFGAO_CANRENAME);

    public bool CanDelete => Has(DesktopFolder.SFGAO_CANDELETE);

    public bool HasProperties => Has(DesktopFolder.SFGAO_HASPROPSHEET);

    /// <summary>Takes drops: a folder, the Recycle Bin, an app (opens what's dropped)...</summary>
    public bool IsDropTarget => Has(DesktopFolder.SFGAO_DROPTARGET);

    private bool Has(uint attribute) => (Attributes & attribute) != 0;

    /// <summary>A native copy of <see cref="IdList"/>, to free with <see cref="Marshal.FreeCoTaskMem"/>.</summary>
    internal nint ToNative()
    {
        nint copy = Marshal.AllocCoTaskMem(IdList.Length);
        Marshal.Copy(IdList, 0, copy, IdList.Length);
        return copy;
    }
}

/// <summary>
/// The desktop namespace, as Explorer's desktop shows it before filtering: the user's and the public Desktop folders
/// merged, plus the system folders registered on the desktop. Every call creates its own COM objects, so any thread
/// can use it.
/// </summary>
public static unsafe class DesktopFolder
{
    internal const uint SFGAO_CANCOPY = 0x1;
    internal const uint SFGAO_CANMOVE = 0x2;
    internal const uint SFGAO_CANRENAME = 0x10;
    internal const uint SFGAO_CANDELETE = 0x20;
    internal const uint SFGAO_HASPROPSHEET = 0x40;
    internal const uint SFGAO_DROPTARGET = 0x100;
    internal const uint SFGAO_LINK = 0x10000;
    internal const uint SFGAO_STREAM = 0x400000;
    internal const uint SFGAO_FOLDER = 0x2000_0000;
    private const uint AttributeMask = SFGAO_CANCOPY | SFGAO_CANMOVE | SFGAO_CANRENAME | SFGAO_CANDELETE
        | SFGAO_HASPROPSHEET | SFGAO_DROPTARGET | SFGAO_LINK | SFGAO_STREAM | SFGAO_FOLDER;

    private const uint SHCONTF_FOLDERS = 0x20;
    private const uint SHCONTF_NONFOLDERS = 0x40;
    private const uint SHCONTF_INCLUDEHIDDEN = 0x80;
    private const uint SHCONTF_INCLUDESUPERHIDDEN = 0x10000;

    private const uint SIGDN_NORMALDISPLAY = 0;
    private const uint SIGDN_PARENTRELATIVEEDITING = 0x8003_1001;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x8002_8000;
    private const uint SIGDN_FILESYSPATH = 0x8005_8000;

    private const uint SHGDN_INFOLDER = 0x1;
    private const uint SHGDN_FOREDITING = 0x1000;

    private const uint SIIGBF_BIGGERSIZEOK = 0x01;

    /// <summary>Lists the desktop's items. Can be slow (it reads the disk): call it off the UI thread.</summary>
    /// <param name="includeHidden">Include hidden files ("Show hidden items" in Explorer).</param>
    /// <param name="includeProtected">Include protected operating system files, such as <c>desktop.ini</c>.</param>
    public static IReadOnlyList<DesktopItem> GetItems(bool includeHidden, bool includeProtected)
    {
        var items = new List<DesktopItem>();
        IShellFolder desktop = Open();
        uint flags = SHCONTF_FOLDERS | SHCONTF_NONFOLDERS
            | (includeHidden ? SHCONTF_INCLUDEHIDDEN : 0)
            | (includeProtected ? SHCONTF_INCLUDESUPERHIDDEN : 0);
        nint enumPointer;
        // S_FALSE with no enumerator means there is nothing to list.
        if (desktop.EnumObjects(0, flags, &enumPointer) != 0 || enumPointer == 0)
            return items;

        IEnumIDList idLists = ComPointer.TakeOwnership<IEnumIDList>(enumPointer);
        nint idList;
        uint fetched;
        while (idLists.Next(1, &idList, &fetched) == 0 && fetched == 1)
        {
            try
            {
                if (Read(idList) is { } item)
                    items.Add(item);
            }
            finally
            {
                Marshal.FreeCoTaskMem(idList);
            }
        }
        return items;
    }

    /// <summary>The item's icon, or a thumbnail for pictures and the like, about <paramref name="size"/> pixels square.</summary>
    public static IconBitmap? GetIcon(DesktopItem item, int size)
    {
        if (CreateItem(item) is not IShellItemImageFactory factory)
            return null;

        return factory.GetImage(new User32.SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out nint bitmap) == 0
            ? IconBitmap.FromBitmap(bitmap)
            : null;
    }

    /// <summary>The arrow Explorer draws over shortcut icons, as an icon-sized image with the arrow in a corner.</summary>
    public static IconBitmap? GetShortcutOverlay(int size)
    {
        var info = new Shell32.SHSTOCKICONINFO { cbSize = (uint)sizeof(Shell32.SHSTOCKICONINFO) };
        if (Shell32.SHGetStockIconInfo(Shell32.SIID_LINK, Shell32.SHGSI_ICONLOCATION, &info) != 0)
            return null;

        nint icon;
        if (Shell32.SHDefExtractIcon(info.szPath, info.iIcon, 0, &icon, null, (uint)size) != 0 || icon == 0)
            return null;
        try
        {
            return IconBitmap.FromIcon(icon);
        }
        finally
        {
            User32.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// Renames an item the way Explorer does: a hidden extension is kept, and the shell reports errors (such as a
    /// name that's taken) in its own dialogs owned by <paramref name="owner"/>.
    /// </summary>
    /// <returns>The renamed item's parsing name, or null if it wasn't renamed.</returns>
    public static string? Rename(nint owner, DesktopItem item, string newName)
    {
        IShellFolder desktop = Open();
        nint idList = item.ToNative();
        nint newIdList = 0;
        try
        {
            fixed (char* name = newName)
            {
                if (desktop.SetNameOf(owner, idList, name, SHGDN_INFOLDER | SHGDN_FOREDITING, &newIdList) != 0 || newIdList == 0)
                    return null;
            }
            return CreateItem(newIdList) is { } renamed ? GetName(renamed, SIGDN_DESKTOPABSOLUTEPARSING) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
            Marshal.FreeCoTaskMem(newIdList);
        }
    }

    internal static IShellFolder Open()
    {
        Marshal.ThrowExceptionForHR(Shell32.SHGetDesktopFolder(out nint folder));
        return ComPointer.TakeOwnership<IShellFolder>(folder);
    }

    private static DesktopItem? Read(nint idList)
    {
        if (CreateItem(idList) is not { } item
            || GetName(item, SIGDN_NORMALDISPLAY) is not { } name
            || GetName(item, SIGDN_DESKTOPABSOLUTEPARSING) is not { } parsingName)
        {
            return null;
        }

        uint attributes;
        if (item.GetAttributes(AttributeMask, &attributes) < 0)
            attributes = 0;

        var copy = new byte[Shell32.ILGetSize(idList)];
        Marshal.Copy(idList, copy, 0, copy.Length);
        return new DesktopItem(
            copy, name, GetName(item, SIGDN_PARENTRELATIVEEDITING) ?? name, parsingName,
            GetName(item, SIGDN_FILESYSPATH), attributes);
    }

    private static IShellItem? CreateItem(DesktopItem item)
    {
        fixed (byte* idList = item.IdList)
            return CreateItem((nint)idList);
    }

    private static IShellItem? CreateItem(nint idList)
    {
        Guid iid = typeof(IShellItem).GUID;
        return Shell32.SHCreateItemFromIDList(idList, iid, out IShellItem item) == 0 ? item : null;
    }

    private static string? GetName(IShellItem item, uint sigdn)
    {
        char* name;
        if (item.GetDisplayName(sigdn, &name) != 0)
            return null;
        try
        {
            return new string(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }
    }
}
