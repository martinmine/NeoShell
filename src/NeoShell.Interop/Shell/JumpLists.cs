using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

public enum JumpListItemKind { Link, Item, Separator }

/// <summary>An entry of a jump list: one of the app's links (a task or its own destination), or a recent file.</summary>
public sealed record JumpListItem(string Title, JumpListItemKind Kind)
{
    /// <summary>A link's persisted data, or an item's ID list.</summary>
    internal byte[] Data { get; init; } = [];
}

public sealed record JumpListCategory(string Title, IReadOnlyList<JumpListItem> Items);

/// <summary>
/// Apps' jump lists, as Explorer shows them on the taskbar: the app's own categories and its Recent or Frequent
/// files (known categories), then its tasks. Pinning entries to a jump list isn't supported.
/// </summary>
public static unsafe class JumpLists
{
    /// <summary>How many recent or frequent files a list shows, as Explorer does by default.</summary>
    public const int MaxKnownItems = 10;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid CLSID_ApplicationDocumentLists = new("86bec222-30f2-47e0-9f25-60d11cd75c28");
    private const int ADLT_RECENT = 0;
    private const int ADLT_FREQUENT = 1;
    private const ushort VT_BOOL = 11;

    // PKEY_Title, PKEY_AppUserModel_IsDestListSeparator
    private static readonly Ole32.PROPERTYKEY TitleKey = new() { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 };
    private static readonly Ole32.PROPERTYKEY SeparatorKey = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 6 };

    // Known folders an implicit AppID starts with, as in shell:AppsFolder's parsing names.
    private static readonly Guid[] s_appIdFolders =
    [
        new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7"), // System
        new("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27"), // SystemX86
        new("F38BF404-1D43-42F2-9305-67DE0B28FC23"), // Windows
        new("6D809377-6AF0-444B-8957-A3773F02200E"), // ProgramFilesX64
        new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E"), // ProgramFilesX86
        new("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB"), // UserProgramFiles
        new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"), // LocalAppData
        new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"), // RoamingAppData
    ];

    /// <summary>The jump list of the app with this AppUserModelID (explicit, or implicit: see <see cref="ImplicitAppId"/>).</summary>
    public static IReadOnlyList<JumpListCategory> Load(string appId)
    {
        string file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Recent\CustomDestinations",
            FileName(appId) + ".customDestinations-ms");
        IReadOnlyList<DestinationCategory> custom;
        try
        {
            custom = File.Exists(file) ? CustomDestinations.Parse(File.ReadAllBytes(file)) : [];
        }
        catch (IOException)
        {
            custom = [];
        }

        // Without a list of its own, an app gets its recent files.
        if (custom.Count == 0)
            custom = [new DestinationCategory(DestinationCategoryKind.Known, "", 2, [])];

        var categories = new List<JumpListCategory>();
        foreach (DestinationCategory category in custom.Where(c => c.Kind != DestinationCategoryKind.Tasks))
        {
            IReadOnlyList<JumpListItem> items = category.Kind == DestinationCategoryKind.Known
                ? KnownItems(appId, category.Known)
                : [.. category.Links.Select(ReadLink).OfType<JumpListItem>().Where(item => item.Kind != JumpListItemKind.Separator)];
            string title = category.Kind == DestinationCategoryKind.Known
                ? (category.Known == 1 ? "Frequent" : "Recent")
                : IndirectString(category.Title);
            if (items.Count > 0)
                categories.Add(new JumpListCategory(title, items));
        }
        // Tasks come last, as in Explorer.
        foreach (DestinationCategory tasks in custom.Where(c => c.Kind == DestinationCategoryKind.Tasks))
        {
            JumpListItem[] items = [.. tasks.Links.Select(ReadLink).OfType<JumpListItem>()];
            if (items.Length > 0)
                categories.Add(new JumpListCategory("Tasks", items));
        }
        return categories;
    }

    /// <summary>
    /// The AppID Windows gives an app that doesn't set one: its path, starting with a known folder's GUID where it
    /// can, e.g. <c>{1AC14E77-…}\notepad.exe</c> for System32's Notepad.
    /// </summary>
    public static string ImplicitAppId(string path) => ImplicitAppId(path, s_appIdFolders.Select(folder => (folder, KnownFolderPath(folder))));

    internal static string ImplicitAppId(string path, IEnumerable<(Guid Folder, string? Path)> folders)
    {
        (Guid Folder, string Path)? best = null;
        foreach ((Guid folder, string? folderPath) in folders)
        {
            if (folderPath is null)
                continue;
            string prefix = folderPath.TrimEnd('\\') + '\\';
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > (best?.Path.Length ?? 0))
                best = (folder, prefix);
        }
        return best is { } match ? $"{{{match.Folder.ToString().ToUpperInvariant()}}}\\{path[match.Path.Length..]}" : path;
    }

    /// <summary>
    /// The name Windows gives the app's jump list files: a CRC-64 of its AppID in capitals, as UTF-16 (polynomial
    /// 0x92C64265D32139A4, bits reflected, starting from all ones), in hex without leading zeros.
    /// </summary>
    internal static string FileName(string appId)
    {
        const ulong Polynomial = 0x92C64265D32139A4;
        ulong crc = ulong.MaxValue;
        foreach (char c in appId.ToUpperInvariant())
        {
            foreach (byte b in (byte[])[(byte)c, (byte)(c >> 8)])
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }
        }
        return crc.ToString("x");
    }

    /// <summary>
    /// Opens the entry as Explorer does: a link through its own context menu (so arguments, working directory and a
    /// packaged app's identity are kept), a file with its default verb. Throws <see cref="COMException"/> on failure.
    /// </summary>
    public static void Open(JumpListItem item, nint owner)
    {
        if (item.Kind == JumpListItemKind.Link)
        {
            IShellLinkW link = LoadLink(item.Data) ?? throw new COMException("The link can't be read");
            var menu = (IContextMenu)link;
            nint handle = User32.CreatePopupMenu();
            try
            {
                const uint FirstCommand = 1;
                Marshal.ThrowExceptionForHR(menu.QueryContextMenu(handle, 0, FirstCommand, 0x7FFF, Shell32.CMF_DEFAULTONLY));
                uint command = User32.GetMenuDefaultItem(handle, 0, 0);
                if (command == uint.MaxValue)
                    throw new COMException("The link has no default command");
                if (!ShellContextMenu.Invoke(menu, owner, command - FirstCommand, point: null))
                    throw new COMException("The link's command failed");
            }
            finally
            {
                User32.DestroyMenu(handle);
            }
        }
        else if (item.Kind == JumpListItemKind.Item)
        {
            fixed (byte* idList = item.Data)
            {
                var info = new Shell32.SHELLEXECUTEINFOW
                {
                    cbSize = (uint)sizeof(Shell32.SHELLEXECUTEINFOW),
                    fMask = Shell32.SEE_MASK_IDLIST | Shell32.SEE_MASK_NOASYNC | Shell32.SEE_MASK_FLAG_NO_UI,
                    hwnd = owner,
                    lpIDList = (nint)idList,
                    nShow = User32.SW_SHOWNORMAL,
                };
                if (!Shell32.ShellExecuteEx(&info))
                    throw new COMException("The file can't be opened", Marshal.GetHRForLastWin32Error());
            }
        }
    }

    /// <summary>The entry's icon: a link's own icon, else its target's; null if there's none.</summary>
    public static IconBitmap? GetIcon(JumpListItem item, int size)
    {
        nint idList = 0;
        try
        {
            if (item.Kind == JumpListItemKind.Link)
            {
                if (LoadLink(item.Data) is not { } link)
                    return null;
                if (LinkIcon(link, size) is { } icon)
                    return icon;
                if (link.GetIDList(&idList) != 0 || idList == 0)
                    return null;
            }
            else if (item.Kind == JumpListItemKind.Item)
            {
                idList = Marshal.AllocCoTaskMem(item.Data.Length);
                Marshal.Copy(item.Data, 0, idList, item.Data.Length);
            }
            else
            {
                return null;
            }

            return Shell32.SHCreateItemFromIDList(idList, typeof(IShellItem).GUID, out IShellItem shellItem) == 0
                && shellItem is IShellItemImageFactory factory
                && factory.GetImage(new User32.SIZE { cx = size, cy = size }, ShellItems.SIIGBF_ICONONLY | ShellItems.SIIGBF_BIGGERSIZEOK, out nint bitmap) == 0
                ? IconBitmap.FromBitmap(bitmap)
                : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    private static IconBitmap? LinkIcon(IShellLinkW link, int size)
    {
        char* path = stackalloc char[260];
        int index;
        if (link.GetIconLocation(path, 260, &index) != 0 || path[0] == 0)
            return null;

        string file = Environment.ExpandEnvironmentVariables(new string(path));
        nint icon;
        fixed (char* filePointer = file)
        {
            if (Shell32.SHDefExtractIcon(filePointer, index, 0, &icon, null, (uint)size) != 0 || icon == 0)
                return null;
        }
        try
        {
            return IconBitmap.FromIcon(icon);
        }
        finally
        {
            User32.DestroyIcon(icon);
        }
    }

    private static JumpListItem? ReadLink(byte[] data)
    {
        if (LoadLink(data) is not { } link)
            return null;

        var store = (IPropertyStore)link;
        if (GetBool(store, SeparatorKey))
            return new JumpListItem("", JumpListItemKind.Separator);

        string? title = GetString(store, TitleKey);
        if (string.IsNullOrEmpty(title))
        {
            char* description = stackalloc char[1024];
            title = link.GetDescription(description, 1024) == 0 ? new string(description) : null;
        }
        return string.IsNullOrEmpty(title) ? null : new JumpListItem(IndirectString(title), JumpListItemKind.Link) { Data = data };
    }

    private static IShellLinkW? LoadLink(byte[] data)
    {
        IShellLinkW link = Ole32.Create<IShellLinkW>(CLSID_ShellLink, Ole32.CLSCTX_INPROC_SERVER);
        nint stream;
        fixed (byte* bytes = data)
            stream = Shlwapi.SHCreateMemStream(bytes, (uint)data.Length);
        if (stream == 0)
            return null;

        try
        {
            return ((IPersistStream)link).Load(stream) == 0 ? link : null;
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static IReadOnlyList<JumpListItem> KnownItems(string appId, int known)
    {
        var lists = Ole32.Create<IApplicationDocumentLists>(CLSID_ApplicationDocumentLists, Ole32.CLSCTX_INPROC_SERVER);
        if (lists.SetAppID(appId) != 0
            || lists.GetList(known == 1 ? ADLT_FREQUENT : ADLT_RECENT, MaxKnownItems, typeof(IObjectArray).GUID, out nint arrayPointer) != 0)
        {
            return [];
        }

        var array = ComPointer.TakeOwnership<IObjectArray>(arrayPointer);
        var items = new List<JumpListItem>();
        if (array.GetCount(out uint count) != 0)
            return items;

        for (uint i = 0; i < count; i++)
        {
            if (array.GetAt(i, typeof(IShellItem).GUID, out nint itemPointer) != 0)
                continue;

            var shellItem = ComPointer.TakeOwnership<IShellItem>(itemPointer);
            if (ShellItems.GetDisplayName(shellItem) is not { } name || ShellItems.GetIDList(shellItem) is not { } idList)
                continue;
            items.Add(new JumpListItem(name, JumpListItemKind.Item) { Data = idList });
        }
        return items;
    }

    private static string? GetString(IPropertyStore store, Ole32.PROPERTYKEY key)
    {
        Ole32.PROPVARIANT value = default;
        try
        {
            return store.GetValue(&key, &value) == 0 && value.vt == Ole32.VT_LPWSTR && value.pointer != 0 ? new string((char*)value.pointer) : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }

    private static bool GetBool(IPropertyStore store, Ole32.PROPERTYKEY key)
    {
        Ole32.PROPVARIANT value = default;
        try
        {
            // A VARIANT_BOOL: -1 for true.
            return store.GetValue(&key, &value) == 0 && value.vt == VT_BOOL && (short)value.pointer != 0;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }

    /// <summary>Resolves a resource reference such as <c>@shell32.dll,-21817</c>; other text is returned as it is.</summary>
    private static string IndirectString(string text)
    {
        if (!text.StartsWith('@'))
            return text;

        char* buffer = stackalloc char[1024];
        return Shlwapi.SHLoadIndirectString(text, buffer, 1024, 0) == 0 ? new string(buffer) : text;
    }

    private static string? KnownFolderPath(Guid folder)
    {
        if (Shell32.SHGetKnownFolderPath(folder, 0, 0, out char* path) != 0)
            return null;
        try
        {
            return new string(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)path);
        }
    }
}
