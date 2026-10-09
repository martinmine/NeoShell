using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
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

    /// <summary>Pinned to the list: shown under Pinned, first.</summary>
    public bool IsPinned { get; init; }

    /// <summary>One of the app's tasks, which can't be pinned or removed.</summary>
    public bool IsTask { get; init; }

    /// <summary>A file or folder (it has a location and properties), rather than a link or a web page.</summary>
    public bool IsFileSystem { get; init; }

    /// <summary>What Explorer shows over the entry: a file's name and folder, a web page's address; null for links.</summary>
    public string? ToolTip { get; init; }
}

public sealed record JumpListCategory(string Title, IReadOnlyList<JumpListItem> Items);

/// <summary>
/// Apps' jump lists, as Explorer shows them on the taskbar and in Start: the entries pinned to the list, the app's own
/// categories and its Recent or Frequent files (known categories), then its tasks; and pinning and removing entries.
/// </summary>
/// <remarks>
/// Pinned, recent and frequent entries come from, and are pinned and removed through, the shell's own automatic
/// destination list (<see cref="IAutomaticDestinationList"/>), as Explorer's jump list broker does, so both shells
/// show and change the same lists.
/// </remarks>
public static unsafe class JumpLists
{
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid CLSID_AutomaticDestinationListBoth = new("656e51bd-cad6-4683-ac07-3e3d50d7f453");
    private static readonly Guid CLSID_DestinationListBoth = new("38fe0cf4-6a59-4729-8e4a-2d580059ede4");
    private const int DLT_PINNED = 0;
    private const int DLT_RECENT = 1;
    private const int DLT_FREQUENT = 2;
    private const int GetListFlags = 1;
    private const int PinLast = -1;
    private const int UnpinIndex = -2;
    private const ushort VT_BOOL = 11;
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

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

        // Without categories of its own (none, or only tasks: Edge's), an app gets its recent files, as in Explorer.
        bool knownAskedFor = !custom.All(category => category.Kind == DestinationCategoryKind.Tasks);
        if (!knownAskedFor)
            custom = [new DestinationCategory(DestinationCategoryKind.Known, "", 2, []), .. custom];
        // A known category other than Frequent (1) or Recent (2), such as Settings' -1, shows nothing.
        DestinationCategory[] shown =
        [
            .. custom.Where(c => c.Kind == DestinationCategoryKind.Custom || (c.Kind == DestinationCategoryKind.Known && c.Known is 1 or 2)),
        ];

        IAutomaticDestinationList? automatic = OpenAutomaticList(appId);
        int maximum = JumpListBudget.Maximum(
            (Registry.GetValue(@"HKEY_CURRENT_USER\" + AdvancedKey, "JumpListItems_Maximum", null)
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + AdvancedKey, "JumpListItems_Maximum", null)) as int?);
        List<JumpListItem> pinned = automatic is null ? [] : AutomaticItems(automatic, DLT_PINNED, maximum, pinned: true);
        // An app's own entries that are pinned show under Pinned only.
        List<JumpListItem>[] own =
        [
            .. shown.Select(category => category.Kind == DestinationCategoryKind.Custom
                ? category.Links.Select(ReadLink).OfType<JumpListItem>()
                    .Where(item => item.Kind != JumpListItemKind.Separator && !(automatic is not null && IsPinned(automatic, item)))
                    .ToList()
                : new List<JumpListItem>()),
        ];
        (int pinnedShown, int[] counts) = JumpListBudget.Split(
            maximum, pinned.Count, [.. shown.Select((category, i) => category.Kind == DestinationCategoryKind.Custom ? own[i].Count : (int?)null)], knownAskedFor);

        var categories = new List<JumpListCategory>();
        if (pinnedShown > 0)
            categories.Add(new JumpListCategory("Pinned", pinned[..pinnedShown]));
        for (int i = 0; i < shown.Length; i++)
        {
            DestinationCategory category = shown[i];
            List<JumpListItem> items = category.Kind == DestinationCategoryKind.Custom
                ? own[i]
                : automatic is null ? [] : RecentOrFrequent(automatic, category.Known == 1 ? DLT_FREQUENT : DLT_RECENT, counts[i], pinned.Count);
            string title = category.Kind == DestinationCategoryKind.Known
                ? (category.Known == 1 ? "Frequent" : "Recent")
                : IndirectString(category.Title);
            if (counts[i] > 0 && items.Count > 0)
                categories.Add(new JumpListCategory(title, items[..Math.Min(counts[i], items.Count)]));
        }
        // Tasks come last, as in Explorer.
        foreach (DestinationCategory tasks in custom.Where(c => c.Kind == DestinationCategoryKind.Tasks))
        {
            JumpListItem[] items = [.. tasks.Links.Select(ReadLink).OfType<JumpListItem>().Select(item => item with { IsTask = true })];
            if (items.Length > 0)
                categories.Add(new JumpListCategory("Tasks", items));
        }
        return categories;
    }

    /// <summary>Pins the entry to the app's list, last, as Explorer's Pin to this list. Throws on failure.</summary>
    public static void Pin(string appId, JumpListItem item) => Change(appId, item, (list, pointer) => list.PinItem(pointer, PinLast));

    /// <summary>Unpins the entry from the app's list. Throws on failure.</summary>
    public static void Unpin(string appId, JumpListItem item) => Change(appId, item, (list, pointer) => list.PinItem(pointer, UnpinIndex));

    /// <summary>
    /// Takes the entry out of the app's list, as Explorer's Remove from this list: out of its recent and frequent items,
    /// and out of its own categories, which the app learns from <c>ICustomDestinationList::GetRemovedDestinations</c>.
    /// Throws on failure.
    /// </summary>
    public static void Remove(string appId, JumpListItem item)
    {
        if (item.Kind == JumpListItemKind.Link)
        {
            var own = Ole32.Create<IInternalCustomDestinationList>(CLSID_DestinationListBoth, Ole32.CLSCTX_INPROC_SERVER);
            Marshal.ThrowExceptionForHR(own.SetApplicationID(appId));
            Marshal.ThrowExceptionForHR(WithPointer(item, pointer => own.RemoveDestination(pointer)));
        }
        // An app's own link isn't among its automatic destinations unless it was pinned once.
        Change(appId, item, (list, pointer) => list.RemoveDestination(pointer), ignoreFailure: item.Kind == JumpListItemKind.Link);
    }

    /// <summary>Opens the file's folder with the file selected, as Explorer's Open file location.</summary>
    public static void OpenLocation(JumpListItem item)
    {
        fixed (byte* idList = item.Data)
            Marshal.ThrowExceptionForHR(Shell32.SHOpenFolderAndSelectItems((nint)idList, 0, null, 0));
    }

    /// <summary>Shows the file's properties, as Explorer's Properties.</summary>
    public static void ShowProperties(JumpListItem item, nint owner)
    {
        fixed (byte* idList = item.Data)
        fixed (char* verb = "properties")
        {
            var info = new Shell32.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Shell32.SHELLEXECUTEINFOW),
                fMask = Shell32.SEE_MASK_INVOKEIDLIST,
                hwnd = owner,
                lpVerb = verb,
                lpIDList = (nint)idList,
                nShow = User32.SW_SHOWNORMAL,
            };
            if (!Shell32.ShellExecuteEx(&info))
                throw new COMException("The properties can't be shown", Marshal.GetHRForLastWin32Error());
        }
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


    private static IAutomaticDestinationList? OpenAutomaticList(string appId)
    {
        if (Ole32.CoCreateInstance(CLSID_AutomaticDestinationListBoth, 0, Ole32.CLSCTX_INPROC_SERVER, typeof(IAutomaticDestinationList).GUID, out nint pointer) != 0)
            return null;
        var list = ComPointer.TakeOwnership<IAutomaticDestinationList>(pointer);
        return list.Initialize(appId, null, null) == 0 ? list : null;
    }

    // The recent or frequent entries that aren't pinned: Explorer asks for as many more as there are pins and leaves
    // those out.
    private static List<JumpListItem> RecentOrFrequent(IAutomaticDestinationList list, int listType, int count, int pinnedCount) =>
        count <= 0 ? [] : [.. AutomaticItems(list, listType, count + pinnedCount, pinned: false).Where(item => !IsPinned(list, item)).Take(count)];

    private static List<JumpListItem> AutomaticItems(IAutomaticDestinationList list, int listType, int maximum, bool pinned)
    {
        var items = new List<JumpListItem>();
        if (maximum <= 0 || list.GetList(listType, maximum, GetListFlags, typeof(IObjectArray).GUID, out nint arrayPointer) != 0)
            return items;

        var array = ComPointer.TakeOwnership<IObjectArray>(arrayPointer);
        if (array.GetCount(out uint count) != 0)
            return items;
        for (uint i = 0; i < count; i++)
        {
            // Files and folders are shell items; links pinned from an app's own categories are shell links.
            if (array.GetAt(i, typeof(IShellItem).GUID, out nint itemPointer) == 0)
            {
                if (FileItem(ComPointer.TakeOwnership<IShellItem>(itemPointer)) is { } item)
                    items.Add(item with { IsPinned = pinned });
            }
            else if (array.GetAt(i, typeof(IPersistStream).GUID, out nint linkPointer) == 0)
            {
                if (SaveLink(ComPointer.TakeOwnership<IPersistStream>(linkPointer)) is { } data && ReadLink(data) is { } link)
                    items.Add(link with { IsPinned = pinned });
            }
        }
        return items;
    }

    private static JumpListItem? FileItem(IShellItem shellItem)
    {
        if (ShellItems.GetDisplayName(shellItem) is not { } name || ShellItems.GetIDList(shellItem) is not { } idList)
            return null;

        // Explorer's tooltip: a file's name and folder, "Doc (C:\Users\…)"; a web page's address.
        string? path = ShellItems.GetPath(idList);
        string? toolTip = path is not null
            ? $"{Path.GetFileNameWithoutExtension(path.TrimEnd('\\'))} ({Path.GetDirectoryName(path.TrimEnd('\\')) ?? path})"
            : ShellItems.GetDisplayName(shellItem, ShellItems.SIGDN_DESKTOPABSOLUTEPARSING);
        return new JumpListItem(name, JumpListItemKind.Item) { Data = idList, IsFileSystem = path is not null, ToolTip = toolTip };
    }

    // A link's persisted data, as an app's list keeps it.
    private static byte[]? SaveLink(IPersistStream link)
    {
        nint stream = Shlwapi.SHCreateMemStream(null, 0);
        if (stream == 0)
            return null;
        try
        {
            if (link.Save(stream, false) != 0 || Shlwapi.IStream_Size(stream, out ulong size) != 0 || Shlwapi.IStream_Reset(stream) != 0)
                return null;
            byte[] data = new byte[size];
            fixed (byte* bytes = data)
                return Shlwapi.IStream_Read(stream, bytes, (uint)size) == 0 ? data : null;
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static bool IsPinned(IAutomaticDestinationList list, JumpListItem item) =>
        WithPointer(item, pointer => list.IsPinned(pointer, out _)) == 0;

    private static void Change(string appId, JumpListItem item, Func<IAutomaticDestinationList, nint, int> change, bool ignoreFailure = false)
    {
        IAutomaticDestinationList list = OpenAutomaticList(appId) ?? throw new COMException($"There's no jump list for {appId}");
        int result = WithPointer(item, pointer => change(list, pointer));
        if (!ignoreFailure)
            Marshal.ThrowExceptionForHR(result);
    }

    // Calls the list with the entry as the shell object it stands for, a shell item or a shell link; returns the call's
    // HRESULT.
    private static int WithPointer(JumpListItem item, Func<nint, int> use)
    {
        void* pointer;
        if (item.Kind == JumpListItemKind.Link)
        {
            IShellLinkW link = LoadLink(item.Data) ?? throw new COMException("The link can't be read");
            pointer = ComInterfaceMarshaller<IShellLinkW>.ConvertToUnmanaged(link);
        }
        else
        {
            nint idList = Marshal.AllocCoTaskMem(item.Data.Length);
            try
            {
                Marshal.Copy(item.Data, 0, idList, item.Data.Length);
                Marshal.ThrowExceptionForHR(Shell32.SHCreateItemFromIDList(idList, typeof(IShellItem).GUID, out IShellItem shellItem));
                pointer = ComInterfaceMarshaller<IShellItem>.ConvertToUnmanaged(shellItem);
            }
            finally
            {
                Marshal.FreeCoTaskMem(idList);
            }
        }
        try
        {
            return use((nint)pointer);
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }
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

    internal static string? KnownFolderPath(Guid folder)
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
