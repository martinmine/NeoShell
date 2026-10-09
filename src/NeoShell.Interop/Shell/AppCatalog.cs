using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>An entry of <c>shell:AppsFolder</c>: what Start lists under All apps.</summary>
/// <param name="Id">
/// The entry's name inside the AppsFolder: an AppUserModelID, or for a plain shortcut a path that may start with a
/// known folder (<c>{GUID}\Tool\tool.exe</c>). <c>shell:AppsFolder\{Id}</c> launches either.
/// </param>
/// <param name="TargetPath">
/// For a shortcut, the file it points to. A shortcut can give its app an AppUserModelID that the app's windows don't
/// carry, so the taskbar needs the executable to match them.
/// </param>
/// <param name="Suite">
/// The Start Menu folder the app is in (<c>System.Tile.SuiteDisplayName</c>: the folder's localized name, its top
/// folder below Programs), which Start shows as a folder; null for an app at the top or a packaged app.
/// </param>
public sealed record AppCatalogEntry(string Name, string Id, string? TargetPath, string? Suite = null);

public static unsafe class AppCatalog
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x8001_8001;
    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    // PKEY_Tile_SuiteDisplayName
    private static readonly Ole32.PROPERTYKEY s_suiteNameKey = new()
    {
        fmtid = new Guid("86D40B4D-9069-443C-819A-2A54090DCCEC"),
        pid = 16,
    };

    /// <summary>Lists every installed app, Win32 and packaged. Slow-ish: call it off the UI thread.</summary>
    public static IReadOnlyList<AppCatalogEntry> Load()
    {
        var entries = new List<AppCatalogEntry>();
        Guid iid = typeof(IShellItem).GUID;
        if (Shell32.SHCreateItemFromParsingName("shell:AppsFolder", 0, iid, out IShellItem folder) != 0)
            return entries;

        Guid handler = BHID_EnumItems;
        Guid enumIid = typeof(IEnumShellItems).GUID;
        nint enumPointer;
        if (folder.BindToHandler(0, &handler, &enumIid, &enumPointer) != 0)
            return entries;

        IEnumShellItems items = ComPointer.TakeOwnership<IEnumShellItems>(enumPointer);
        nint itemPointer;
        uint fetched;
        while (items.Next(1, &itemPointer, &fetched) == 0 && fetched == 1)
        {
            IShellItem item = ComPointer.TakeOwnership<IShellItem>(itemPointer);
            if (GetName(item, SIGDN_NORMALDISPLAY) is { } name && GetName(item, SIGDN_PARENTRELATIVEPARSING) is { } id)
                entries.Add(new AppCatalogEntry(name, id, ShellItems.GetString(item, ShellItems.LinkTargetPathKey), ShellItems.GetString(item, s_suiteNameKey) is { Length: > 0 } suite ? suite : null));
        }
        return entries;
    }

    /// <summary>
    /// Whether an entry ID is an AppUserModelID, rather than the path of a shortcut's target (or a URL).
    /// AppUserModelIDs never contain a backslash or a colon.
    /// </summary>
    public static bool IsAppUserModelId(string id) => !id.Contains('\\') && !id.Contains(':');

    /// <summary>The file system path of a path-like entry ID, with a leading known folder expanded; null otherwise.</summary>
    public static string? ResolvePath(string id)
    {
        if (IsAppUserModelId(id))
            return null;
        if (SplitKnownFolder(id) is not ({ } folder, { } relativePath))
            return id;

        if (Shell32.SHGetKnownFolderPath(folder, 0, 0, out char* path) != 0)
            return null;
        try
        {
            return Path.Combine(new string(path), relativePath);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)path);
        }
    }

    /// <summary>Splits <c>{GUID}\relative\path</c> into the known folder and the path in it; null for anything else.</summary>
    internal static (Guid Folder, string RelativePath)? SplitKnownFolder(string id)
    {
        int end = id.IndexOf("}\\", StringComparison.Ordinal);
        return id.StartsWith('{') && end > 0 && Guid.TryParse(id.AsSpan(0, end + 1), out Guid folder)
            ? (folder, id[(end + 2)..])
            : null;
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
