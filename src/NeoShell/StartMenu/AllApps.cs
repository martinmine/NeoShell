using System.Globalization;
using System.Text;
using NeoShell.Interop.Shell;
using NeoShell.Settings;

namespace NeoShell.StartMenu;

/// <summary>
/// An app in All apps: its tile id in Explorer's Start, category, use, whether it shows "New" or "System", and the Start
/// Menu folder it's in (its suite).
/// </summary>
public sealed record AllAppsEntry(PinnedApp App, string TileId, int Category, double Usage, bool IsNew = false, bool IsSystem = false, string? Suite = null);

/// <summary>What All lists: an app, or a folder of apps (a Start Menu folder with two apps or more), its apps by name.</summary>
public sealed record AllAppsItem(string Name, IReadOnlyList<AllAppsEntry> Apps, bool IsFolder)
{
    public static AllAppsItem Of(AllAppsEntry app) => new(app.App.DisplayName, [app], false);
}

/// <summary>
/// How Explorer's 25H2 Start sorts All apps into categories and orders them, and the letters of the name views
/// (read from StartMenu.dll's <c>CategoryProvider</c> and StartTileData.dll; see docs/design/start-menu.md,
/// "All apps").
/// </summary>
public static class AllApps
{
    public const int Other = 0;
    public const int MostUsedCount = 6;
    /// <summary>The "Most used" group's key among the letters.</summary>
    public const string MostUsed = "Most used";
    /// <summary>Names that start with neither a Latin letter, a digit nor a symbol: Explorer's globe.</summary>
    public const string OtherScripts = "";

    // Start's AppCategory values (its AllApps_Category_N strings); 0 is Other.
    private static readonly string[] s_names =
    [
        "Other", "Accessibility", "Communication", "Games", "Travel", "Security", "Personalization", "Photo & Video",
        "Social", "Utilities & Tools", "Kids & Family", "Medical", "Health & Fitness", "Productivity", "Books & Reference",
        "Developer Tools", "Entertainment", "Music", "Personal Finance", "Education", "Business", "Navigation & Maps",
        "Graphics", "Multimedia Design", "Government & politics", "News & Weather", "Sports", "Lifestyle", "Shopping",
        "Food & Dining", "Creativity", "Information & Reading",
    ];

    // CategoryProvider::PruneAndCombineCategories: these always go into the first, …
    private static readonly (int Into, int[] From)[] s_merged =
    [
        (30, [7, 23, 22, 6]),
        (13, [2, 5]),
        (31, [25, 24, 14, 21]),
    ];

    // … these only while they have fewer than three apps. Then any category with two apps or fewer goes into Other.
    private static readonly (int Into, int[] From)[] s_mergedWhenSmall =
    [
        (16, [3, 26, 17]),
        (27, [28, 12, 29, 11, 18, 4]),
    ];

    // StartTileData's ShellLists: apps Start marks as system, or not, whatever their package says.
    private static readonly Dictionary<string, bool> s_systemOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.ApplicationCompatibilityEnhancements_8wekyb3d8bbwe!Microsoft.ApplicationCompatibilityEnhancements"] = true,
        ["Microsoft.AutoSuperResolution2_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.DesktopAppInstaller_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.GetHelp_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.Getstarted_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.MixedReality.Portal_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.SecHealthUI_8wekyb3d8bbwe!SecHealthUI"] = true,
        ["Microsoft.WidgetsPlatformRuntime_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.Windows.DevHome_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.WindowsCamera_8wekyb3d8bbwe!App"] = false,
        ["Microsoft.WindowsStore_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.XboxGamingOverlay_8wekyb3d8bbwe!App"] = true,
        ["Microsoft.YourPhone_8wekyb3d8bbwe!App"] = true,
        ["MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe!wsl"] = true,
        ["MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy!App"] = true,
        ["MicrosoftWindows.CrossDevice_cw5n1h2txyewy!App"] = true,
        ["MicrosoftWindows.WindowsSandbox_cw5n1h2txyewy!App"] = true,
        ["MicrosoftWindows.Client.CBS_cw5n1h2txyewy!WindowsBackup"] = true,
        ["Microsoft.Windows.Explorer"] = true,
        ["Microsoft.Windows.MediaPlayer32"] = false,
        ["Microsoft.Windows.RemoteDesktop"] = false,
        ["Microsoft.WSL"] = true,
        ["MSEdge"] = false,
        // Not in the lists, and not in the Windows folder (Control Panel opens it), yet Start marks it.
        ["Microsoft.Windows.AdministrativeTools"] = true,
    };

    // StartTileData's block lists: packages pushed into the Windows Accessories folder (and so hidden with it), Dev Home
    // only while the user hasn't said they're a developer; Click to Do while its entry point is hidden.
    private const string PowerAutomate = "Microsoft.PowerAutomateDesktop_8wekyb3d8bbwe";
    private const string DevHome = "Microsoft.Windows.DevHome_8wekyb3d8bbwe";
    private const string ClickToDo = "MicrosoftWindows.Client.CoreAI_cw5n1h2txyewy!ClickToDoApp";

    /// <summary>
    /// Whether Explorer's Start leaves the app out of All: the apps of the folders Windows Tools holds (Windows
    /// Accessories, Windows Tools, Windows System, Windows PowerShell), and those its block lists hide.
    /// </summary>
    public static bool IsHidden(string catalogId, string? suite, StartHiddenApps hidden)
    {
        int bang = catalogId.IndexOf('!');
        string family = bang > 0 ? catalogId[..bang] : "";
        if (family.Equals(PowerAutomate, StringComparison.OrdinalIgnoreCase)
            || (family.Equals(DevHome, StringComparison.OrdinalIgnoreCase) && !hidden.DeveloperIntent)
            || (catalogId.Equals(ClickToDo, StringComparison.OrdinalIgnoreCase) && hidden.ClickToDoHidden))
            return true;
        return suite is not null && hidden.Suites.Contains(suite);
    }

    /// <summary>
    /// The apps as All lists them, as StartTileData's <c>AppsListGeneratedCollection</c> builds them: apps of the same
    /// Start Menu folder (by name, any case) become a folder once there are two of them; an app alone in its folder
    /// stays an app. A folder's apps go by name.
    /// </summary>
    public static IReadOnlyList<AllAppsItem> Items(IEnumerable<AllAppsEntry> apps)
    {
        var items = new List<AllAppsItem>();
        foreach (IGrouping<string, AllAppsEntry> suite in apps.Where(app => app.Suite is not null).GroupBy(app => app.Suite!, StringComparer.OrdinalIgnoreCase))
        {
            List<AllAppsEntry> members = [.. suite.OrderBy(app => app.App.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
            if (members.Count >= 2)
                items.Add(new AllAppsItem(suite.First().Suite!, members, true));
            else
                items.Add(AllAppsItem.Of(members[0]));
        }
        items.AddRange(apps.Where(app => app.Suite is null).Select(AllAppsItem.Of));
        return items;
    }

    /// <summary>A category's name, as Start shows it on its card.</summary>
    public static string CategoryName(int category) =>
        category > 0 && category < s_names.Length ? s_names[category] : s_names[Other];

    /// <summary>The app's id in Explorer's Start: <c>P~</c> and the AppUserModelID for a packaged app, else <c>W~</c> and its AppsFolder id.</summary>
    public static string TileId(string catalogId) => (PackagedApps.IsPackagedAppId(catalogId) ? "P~" : "W~") + catalogId;

    /// <summary>
    /// The app's category: what Explorer's Start saved for it (Windows' web service's answer). An app it hasn't
    /// categorized is Other there, so it is here too. Only when Start has saved none at all (it has never run for the
    /// user) are Windows' local mappings read as StartTileData reads them: a packaged app by its package family name; a
    /// desktop app by its path below one of <paramref name="folders"/> (the Windows folder, Program Files and
    /// Program Files (x86)), else by its AppsFolder id. Other when none knows it.
    /// </summary>
    public static int CategoryFor(
        string catalogId, string? path, IReadOnlyDictionary<string, int> saved, IReadOnlyDictionary<string, int> mappings, IReadOnlyList<string> folders)
    {
        if (saved.Count > 0)
            return saved.TryGetValue(TileId(catalogId), out int known) ? known : Other;

        int category;

        int bang = catalogId.IndexOf('!');
        if (bang > 0)
            return mappings.TryGetValue(catalogId[..bang], out category) ? category : Other;

        if (path is not null && folders.FirstOrDefault(folder => folder.Length > 0 && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) is { } root
            && mappings.TryGetValue(path[root.Length..], out category))
            return category;
        return mappings.TryGetValue(catalogId, out category) ? category : Other;
    }

    /// <summary>
    /// Whether Start shows "System" under the app: Start's own list, else a packaged app from Windows itself, else (the
    /// app resolver's call, which Start asks for desktop apps) a program in the Windows folder.
    /// </summary>
    public static bool IsSystem(string catalogId, string? path, IReadOnlySet<string> systemFamilies, string windowsFolder)
    {
        if (s_systemOverrides.TryGetValue(catalogId, out bool system))
            return system;
        int bang = catalogId.IndexOf('!');
        if (bang > 0)
            return systemFamilies.Contains(catalogId[..bang]);
        return path is not null && windowsFolder.Length > 0 && path.StartsWith(windowsFolder.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether Start shows "New" under the app: Explorer's Start hasn't seen it (it records a first-seen time once the
    /// app is started from Start) and it hasn't been opened from NeoShell's Start either. Nothing is new when
    /// Explorer's Start has kept no record at all.
    /// </summary>
    public static bool IsNew(string tileId, IReadOnlySet<string>? seen, IEnumerable<string> opened) =>
        seen is not null && !seen.Contains(tileId) && !opened.Contains(tileId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How much each app is used, Start's "relevance": its starts plus its minutes in front, over every UserAssist
    /// value that is the app (by AppUserModelID, or by path). Explorer's Start keeps its own figure, which it doesn't
    /// share; this one orders the same apps the same way on the test machine.
    /// </summary>
    public static IReadOnlyDictionary<PinnedApp, double> UsageScores(IEnumerable<PinnedApp> apps, IEnumerable<AppUsage> usage)
    {
        List<PinnedApp> catalog = [.. apps];
        var scores = new Dictionary<PinnedApp, double>();
        foreach (AppUsage entry in usage)
        {
            PinnedApp? app = AppCatalog.IsAppUserModelId(entry.Id)
                ? StartCatalog.Find(catalog, entry.Id, null)
                : StartCatalog.Find(catalog, null, AppCatalog.ResolvePath(entry.Id));
            if (app is not null)
                scores[app] = scores.GetValueOrDefault(app) + entry.Runs + entry.FocusTime.TotalMinutes;
        }
        return scores;
    }

    /// <summary>
    /// The category cards in Start's order, each with its items in order: categories merged as Start merges them, the
    /// most used first (by the use of their two most used items), items by use then name. A folder goes where its first
    /// app with a category goes (<c>TryGetCategoriesForTiles</c>), and is as used as its first app.
    /// </summary>
    public static IReadOnlyList<(int Category, IReadOnlyList<AllAppsItem> Items)> Categories(IEnumerable<AllAppsEntry> apps)
    {
        var groups = new Dictionary<int, List<AllAppsItem>>();
        foreach (AllAppsItem item in Items(apps))
            Into(groups, item.Apps.Select(app => app.Category).FirstOrDefault(c => c > 0 && c < s_names.Length, Other)).Add(item);

        Merge(groups, s_merged, always: true);
        Merge(groups, s_mergedWhenSmall, always: false);
        // PruneAndCombineCategories: two items or fewer go into Other, but a category that is one folder of three apps
        // or more shows the folder's apps instead.
        foreach (int category in groups.Keys.Where(c => c != Other && groups[c].Count <= 2).ToList())
        {
            if (groups[category] is [{ IsFolder: true, Apps.Count: >= 3 } folder])
            {
                groups[category] = [.. folder.Apps.Select(AllAppsItem.Of)];
                continue;
            }
            Into(groups, Other).AddRange(groups[category]);
            groups.Remove(category);
        }

        return [.. groups
            .Where(group => group.Value.Count > 0)
            .Select(group => (Category: group.Key, Items: (IReadOnlyList<AllAppsItem>)[.. ByUse(group.Value)]))
            .OrderByDescending(group => group.Items.Take(2).Sum(item => Rank(item.Apps[0].Usage)))
            .ThenBy(group => CategoryName(group.Category), StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void Merge(Dictionary<int, List<AllAppsItem>> groups, (int Into, int[] From)[] merges, bool always)
    {
        foreach ((int into, int[] from) in merges)
        {
            List<AllAppsItem> target = Into(groups, into);
            foreach (int source in from)
            {
                if (source != into && groups.TryGetValue(source, out List<AllAppsItem>? apps) && (always || apps.Count < 3))
                {
                    target.AddRange(apps);
                    groups.Remove(source);
                }
            }
        }
    }

    private static List<AllAppsItem> Into(Dictionary<int, List<AllAppsItem>> groups, int category)
    {
        if (!groups.TryGetValue(category, out List<AllAppsItem>? list))
            groups[category] = list = [];
        return list;
    }

    // Start ranks by its relevance in thousandths, rounded; apps of equal rank go by name.
    private static int Rank(double usage) => (int)Math.Round(usage);

    private static IEnumerable<AllAppsEntry> ByUse(IEnumerable<AllAppsEntry> apps) => apps
        .OrderByDescending(app => Rank(app.Usage))
        .ThenBy(app => app.App.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    // A folder is as used as its first app (AllAppsSuiteViewModel::Rank).
    private static IEnumerable<AllAppsItem> ByUse(IEnumerable<AllAppsItem> items) => items
        .OrderByDescending(item => Rank(item.Apps[0].Usage))
        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>"Most used" in the name views: the six most used apps that have been used at all.</summary>
    public static IReadOnlyList<AllAppsEntry> MostUsedApps(IEnumerable<AllAppsEntry> apps) =>
        [.. ByUse(apps.Where(app => Rank(app.Usage) > 0)).Take(MostUsedCount)];

    /// <summary>
    /// The letter an app is listed under: its first letter without accents, # for a digit, &amp; for anything else,
    /// and Explorer's globe for other scripts.
    /// </summary>
    public static string LetterFor(string name)
    {
        if (name.Length == 0)
            return "&";
        char first = name.Normalize(NormalizationForm.FormD)[0];
        if (char.IsAsciiDigit(first))
            return "#";
        if (char.IsAsciiLetter(first))
            return char.ToUpperInvariant(first).ToString();
        return char.IsLetter(first) ? OtherScripts : "&";
    }

    /// <summary>The letters in Explorer's order: Most used, &amp;, #, A to Z, the globe.</summary>
    public static IReadOnlyList<string> Letters => s_letters;

    private static readonly string[] s_letters =
        [MostUsed, "&", "#", .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()), OtherScripts];

    /// <summary>A letter's place in <see cref="Letters"/>.</summary>
    public static int LetterOrder(string letter)
    {
        int index = Array.IndexOf(s_letters, letter);
        return index < 0 ? s_letters.Length : index;
    }

    /// <summary>The name views' groups: the apps and folders by name under their letters, in <see cref="Letters"/> order.</summary>
    public static IReadOnlyList<(string Letter, IReadOnlyList<AllAppsItem> Items)> ByLetter(IEnumerable<AllAppsEntry> apps) =>
        [.. Items(apps)
            .OrderBy(item => item.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            // An app before a folder of the same name, as Explorer's ("Visual Studio").
            .ThenBy(item => item.IsFolder)
            .GroupBy(item => LetterFor(item.Name))
            .OrderBy(group => LetterOrder(group.Key))
            .Select(group => (group.Key, (IReadOnlyList<AllAppsItem>)[.. group]))];
}
