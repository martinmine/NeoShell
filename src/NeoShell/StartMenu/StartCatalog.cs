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

    /// <summary>
    /// The catalog app with this AppUserModelID or, failing that, this executable or shortcut target; null if
    /// neither is in the catalog.
    /// </summary>
    public static PinnedApp? Find(IEnumerable<PinnedApp> apps, string? appUserModelId, string? path) =>
        (appUserModelId is null ? null : apps.FirstOrDefault(app => string.Equals(app.AppUserModelId, appUserModelId, StringComparison.OrdinalIgnoreCase)))
        ?? (path is null ? null : apps.FirstOrDefault(app => string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Explorer's Start pins as catalog apps, in Explorer's order. Pins of apps since removed are dropped.</summary>
    public static IReadOnlyList<PinnedApp> FromExplorerPins(IEnumerable<PinnedApp> apps, IEnumerable<ExplorerStartPin> pins) =>
        [.. pins.Select(pin => Find(apps, pin.AppUserModelId, pin.TargetPath)).OfType<PinnedApp>().Distinct()];

    /// <summary>
    /// The catalog apps started most recently, newest first. An app can be recorded both by its AppUserModelID and
    /// by its executable; it's listed once.
    /// </summary>
    public static IReadOnlyList<(PinnedApp App, DateTime LastRun)> Recent(IEnumerable<PinnedApp> apps, IEnumerable<AppUsage> usage, int count) =>
        [.. usage
            .OrderByDescending(entry => entry.LastRun)
            .Select(entry => (App: AppCatalog.IsAppUserModelId(entry.Id) ? Find(apps, entry.Id, null) : Find(apps, null, AppCatalog.ResolvePath(entry.Id)), entry.LastRun))
            .Where(recent => recent.App is not null)
            .DistinctBy(recent => recent.App)
            .Take(count)
            .Select(recent => (recent.App!, recent.LastRun))];

    /// <summary>When a recent app was last started, as Windows' Start says it: "Just now", "30m ago", "5h ago", else the date.</summary>
    public static string LastRunText(DateTime lastRun, DateTime now)
    {
        TimeSpan ago = now - lastRun.ToLocalTime();
        return ago.TotalMinutes < 1 ? "Just now"
            : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes}m ago"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours}h ago"
            : lastRun.ToLocalTime().ToString("M");
    }

    public static string GroupFor(IndexResult result) =>
        result.Kinds.Contains("folder", StringComparer.OrdinalIgnoreCase) ? Folders
        : result.Kinds.Any(s_documentKinds.Contains) ? Documents
        : Other;

    /// <summary>The letter an app is listed under in All apps: its first letter, or # for anything else.</summary>
    public static string LetterFor(string name) =>
        name.Length > 0 && char.IsLetter(name[0]) ? char.ToUpper(name[0]).ToString() : "#";
}
