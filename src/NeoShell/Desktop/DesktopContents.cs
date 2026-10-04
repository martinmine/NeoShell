using System.Globalization;
using NeoShell.Interop.Shell;
using NeoShell.Settings;

namespace NeoShell.Desktop;

/// <summary>An item of the desktop with what sorting needs to know about it.</summary>
/// <param name="SystemIcon">Position among <see cref="DesktopContents.SystemIcons"/>, or -1 for a file or folder.</param>
/// <param name="Size">File size in bytes; 0 for folders and system icons.</param>
public sealed record DesktopEntry(DesktopItem Item, int SystemIcon, long Size, DateTime Modified);

/// <summary>The folders the desktop shows the contents of.</summary>
public sealed record DesktopLocations(string UserDesktop, string PublicDesktop, string UserProfile)
{
    public static DesktopLocations Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

/// <summary>Which items of the desktop namespace get an icon, and in which order, the way Explorer decides.</summary>
public static class DesktopContents
{
    private const string UsersFiles = "{59031A47-3F72-44A7-89C5-5595FE6B30EE}";

    /// <summary>
    /// The icons of "Desktop icon settings", in Explorer's order, by CLSID, and whether each shows when the user
    /// never chose. Choices are in <c>HideDesktopIcons\NewStartPanel</c>: 1 hides, 0 shows.
    /// </summary>
    public static readonly IReadOnlyList<(string Clsid, bool ShownByDefault)> SystemIcons =
    [
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // This PC
        (UsersFiles, false),
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Network
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true), // Recycle Bin
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Control Panel
    ];

    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>
    /// The position of a system icon in <see cref="SystemIcons"/>, or -1. The user's files folder has its path as
    /// parsing name, not its CLSID.
    /// </summary>
    public static int SystemIconIndex(DesktopItem item, DesktopLocations locations)
    {
        string clsid = string.Equals(item.ParsingName, locations.UserProfile, StringComparison.OrdinalIgnoreCase)
            ? UsersFiles
            : item.ParsingName.StartsWith("::", StringComparison.Ordinal) ? item.ParsingName[2..] : "";
        for (int i = 0; i < SystemIcons.Count; i++)
        {
            if (string.Equals(SystemIcons[i].Clsid, clsid, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Whether the desktop shows an item: a file or folder directly in one of the Desktop folders, or a system icon
    /// the user didn't hide. Anything else in the namespace (Libraries, OneDrive, drives…) belongs to the navigation
    /// pane, not the desktop.
    /// </summary>
    /// <param name="hiddenSystemIcons">The <c>HideDesktopIcons\NewStartPanel</c> values by CLSID.</param>
    public static bool IsShown(DesktopItem item, DesktopLocations locations, IReadOnlyDictionary<string, int> hiddenSystemIcons)
    {
        int systemIcon = SystemIconIndex(item, locations);
        if (systemIcon >= 0)
        {
            (string clsid, bool shownByDefault) = SystemIcons[systemIcon];
            return hiddenSystemIcons.TryGetValue(clsid, out int hidden) ? hidden == 0 : shownByDefault;
        }

        string? folder = item.Path is null ? null : System.IO.Path.GetDirectoryName(item.Path);
        return folder is not null
            && (string.Equals(folder, locations.UserDesktop, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, locations.PublicDesktop, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Orders icons like Explorer's Sort by: system icons first in their fixed order, then folders, then files, each
    /// by the chosen key and then by name. Names compare as Explorer does, with numbers by value ("2" before "10").
    /// </summary>
    public static IReadOnlyList<DesktopEntry> Sort(IEnumerable<DesktopEntry> entries, DesktopSortOrder order)
    {
        var sorted = entries
            .OrderBy(entry => entry.SystemIcon < 0)
            .ThenBy(entry => entry.SystemIcon)
            .ThenBy(entry => !entry.Item.IsFolder);
        sorted = order switch
        {
            DesktopSortOrder.Size => sorted.ThenBy(entry => entry.Size),
            DesktopSortOrder.ItemType => sorted.ThenBy(entry => TypeOf(entry.Item), StringComparer.OrdinalIgnoreCase),
            DesktopSortOrder.DateModified => sorted.ThenByDescending(entry => entry.Modified),
            _ => sorted,
        };
        return [.. sorted.ThenBy(entry => entry.Item.Name, NameComparer)];
    }

    private static string TypeOf(DesktopItem item) =>
        item.IsFolder || item.Path is null ? "" : System.IO.Path.GetExtension(item.Path);
}
