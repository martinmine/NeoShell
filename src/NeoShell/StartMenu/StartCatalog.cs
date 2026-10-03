using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Settings;

namespace NeoShell.StartMenu;

/// <summary>How Start turns catalog entries and search results into things to launch, pin and group.</summary>
public static class StartCatalog
{
    /// <summary>Search result groups, in the order they're shown.</summary>
    public const string Apps = "Apps";
    public const string Documents = "Documents";
    public const string Folders = "Folders";
    public const string Other = "Other";

    private static readonly HashSet<string> s_documentKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "document", "picture", "music", "video", "movie", "note", "presentation",
    };

    /// <summary>
    /// An app as Start and the taskbar keep it: launched by AppUserModelID when it has one, and matched to its
    /// windows by AppUserModelID or, for windows without one, by the executable the entry points to.
    /// </summary>
    public static PinnedApp ToPinnedApp(AppCatalogEntry entry) => new(
        entry.Name,
        AppCatalog.IsAppUserModelId(entry.Id) ? entry.Id : null,
        AppCatalog.ResolvePath(entry.Id) ?? entry.TargetPath);

    public static string GroupFor(IndexResult result) =>
        result.Kinds.Contains("folder", StringComparer.OrdinalIgnoreCase) ? Folders
        : result.Kinds.Any(s_documentKinds.Contains) ? Documents
        : Other;

    /// <summary>The letter an app is listed under in All apps: its first letter, or # for anything else.</summary>
    public static string LetterFor(string name) =>
        name.Length > 0 && char.IsLetter(name[0]) ? char.ToUpper(name[0]).ToString() : "#";
}
