using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Settings;
using NeoShell.Taskbar;

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

    /// <summary>
    /// The catalog app with this AppUserModelID or, failing that, this executable or shortcut target; null if
    /// neither is in the catalog.
    /// </summary>
    public static PinnedApp? Find(IEnumerable<PinnedApp> apps, string? appUserModelId, string? path) =>
        (appUserModelId is null ? null : apps.FirstOrDefault(app => string.Equals(app.AppUserModelId, appUserModelId, StringComparison.OrdinalIgnoreCase)))
        ?? (path is null ? null : apps.FirstOrDefault(app => string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Explorer's pins as catalog apps, in Explorer's order. Pins of apps since removed are dropped.</summary>
    public static IReadOnlyList<PinnedApp> FromExplorerPins(IEnumerable<PinnedApp> apps, IEnumerable<ExplorerPin> pins) =>
        [.. pins.Select(pin => Find(apps, pin.AppUserModelId, pin.TargetPath)).OfType<PinnedApp>().Distinct()];

    /// <summary>Imported pins after the ones made in NeoShell, which stay first; apps already pinned aren't added again.</summary>
    public static IReadOnlyList<PinnedApp> AddImported(IReadOnlyList<PinnedApp> pinned, IEnumerable<PinnedApp> imported) =>
        [.. pinned, .. imported.Where(app => !pinned.Any(p => TaskGrouping.SameApp(p, app)))];

    public static string GroupFor(IndexResult result) =>
        result.Kinds.Contains("folder", StringComparer.OrdinalIgnoreCase) ? Folders
        : result.Kinds.Any(s_documentKinds.Contains) ? Documents
        : Other;
}
