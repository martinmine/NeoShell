using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace NeoShell.Settings;

public enum TaskbarAlignment { Center, Left }

public enum CombineButtons { Always, WhenFull, Never }

public enum TrayMode { ShowAll, Overflow }

public enum Backdrop { Acrylic, Mica, Translucent, Transparent }

public enum DesktopSortOrder { Name, Size, ItemType, DateModified }

/// <summary>A cell of the desktop's icon grid: columns from the left, rows from the top.</summary>
public readonly record struct GridCell(int Column, int Row);

public enum WidgetKind { Profile, Resources, Pictures, Media, Weather, Notes, Wireless, Windows }

/// <summary>
/// One widget: in the sidebar, in the order of <see cref="ShellSettings.Widgets"/>, or floating on the desktop. Its
/// options are those of its kind; null ones take the widget's default.
/// </summary>
public sealed record WidgetSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    /// <summary>Where a floating widget's top-left corner is, in screen pixels; null while it's in the sidebar.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }
    /// <summary>A floating widget's width, and the height of the part that can be resized (a note's text), in effective pixels.</summary>
    public double? FloatingWidth { get; set; }
    public double? ContentHeight { get; set; }

    // Profile
    public bool? ShowSeconds { get; set; }

    // Resources: each graph's colour, as #RRGGBB, and whether drives and network adapters show one by one.
    public string? CpuColor { get; set; }
    public string? GpuColor { get; set; }
    public string? MemoryColor { get; set; }
    public string? DiskColor { get; set; }
    public string? NetworkColor { get; set; }
    public bool? ShowEachDrive { get; set; }
    public bool? ShowEachAdapter { get; set; }

    // Pictures
    public string? PictureFolder { get; set; }
    public int? SlideSeconds { get; set; }

    // Media
    public bool? ShowArtwork { get; set; }

    // Weather: a fixed place; without one, the computer's location.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Notes
    public double? FontSize { get; set; }

    // Wireless devices: how often their batteries are read, in minutes.
    public int? PollMinutes { get; set; }

    [JsonIgnore]
    public bool IsFloating => X is not null && Y is not null;
}

/// <summary>A pinned app: launched through its AppUserModelID when it has one, otherwise through its path.</summary>
public sealed record PinnedApp(string DisplayName, string? AppUserModelId = null, string? Path = null, string? Arguments = null);

/// <summary>A pin in Start's grid: an app, or a folder of apps.</summary>
public sealed record StartPin(PinnedApp? App = null, StartFolder? Folder = null);

/// <summary>A folder of pinned apps in Start. An empty name shows as "Folder", as in Explorer.</summary>
/// <param name="Id">Tells the folder apart from others with the same name and apps, as it changes.</param>
public sealed record StartFolder(string Id, string Name, IReadOnlyList<PinnedApp> Apps);

/// <summary>User settings. Defaults match the Windows 11 taskbar.</summary>
/// <remarks>
/// Properties are <c>set</c>, not <c>init</c>: the JSON source generator assigns every init property when it
/// constructs the record, so values missing from the file would become <c>default</c> instead of these defaults.
/// </remarks>
public sealed record ShellSettings
{
    public TaskbarAlignment TaskbarAlignment { get; set; } = TaskbarAlignment.Center;
    public CombineButtons CombineButtons { get; set; } = CombineButtons.Always;
    public bool AutoHide { get; set; }
    public bool ShowOnAllDisplays { get; set; } = true;
    /// <summary>
    /// The former search button toggle, which Explorer's search setting replaces: read once (hidden, it hides
    /// Explorer's search too, see <see cref="Taskbar.TaskbarSearch"/>) and cleared.
    /// </summary>
    public bool? ShowSearchButton { get; set; }
    /// <summary>
    /// The tray's former setting, all icons on the taskbar or behind the chevron, which Explorer's per-icon settings
    /// replace: read once as the shell (<see cref="TrayMode.ShowAll"/> then shows every icon) and cleared.
    /// </summary>
    public TrayMode? TrayMode { get; set; }
    public Backdrop TaskbarBackdrop { get; set; } = Backdrop.Acrylic;
    public IReadOnlyList<PinnedApp> PinnedTaskbarApps { get; set; } = [];
    public IReadOnlyList<StartPin> StartPins { get; set; } = [];
    /// <summary>Start's former pins, apps only, which <see cref="StartPins"/> replaces: read once and cleared.</summary>
    public IReadOnlyList<PinnedApp>? PinnedStartApps { get; set; }
    /// <summary>Whether Explorer's Start pins have been added to <see cref="StartPins"/>; done once.</summary>
    public bool ExplorerStartPinsImported { get; set; }
    /// <summary>Whether Explorer's taskbar pins have been added to <see cref="PinnedTaskbarApps"/>; done once.</summary>
    public bool ExplorerTaskbarPinsImported { get; set; }
    /// <summary>Start's size in effective pixels, as the user left it by dragging a corner.</summary>
    public double StartMenuWidth { get; set; } = 832;
    public double StartMenuHeight { get; set; } = 860;
    /// <summary>
    /// Apps opened from NeoShell's Start, by their id in Explorer's Start: like Explorer's Start, All apps stops
    /// showing "New" under them. (Explorer's own record is its tile store, which NeoShell only reads.)
    /// </summary>
    public IReadOnlyList<string> StartAppsOpened { get; set; } = [];
    /// <summary>The calendar under the notification center folded away to its heading, as the user left it.</summary>
    public bool CalendarCollapsed { get; set; }
    /// <summary>The length of a focus session, as last chosen in the calendar.</summary>
    public int FocusMinutes { get; set; } = 30;
    public DesktopSortOrder DesktopSortOrder { get; set; } = DesktopSortOrder.Name;
    /// <summary>
    /// Where the desktop's icons are, by parsing name, as Explorer remembers them: kept for every icon once placed, so
    /// moving one leaves the others where they are. Emptied by Sort by; not used while icons are auto-arranged.
    /// </summary>
    public IReadOnlyDictionary<string, GridCell> DesktopIconPositions { get; set; } = NoIconPositions;
    public bool ShowWidgetSidebar { get; set; } = true;
    /// <summary>The sidebar's own backdrop behind its widgets; without it, only the widgets show, each on its own.</summary>
    public bool ShowWidgetPanel { get; set; } = true;
    /// <summary>The sidebar's width in effective pixels, as the user left it by dragging its edge.</summary>
    public double WidgetSidebarWidth { get; set; } = 320;
    public IReadOnlyList<WidgetSettings> Widgets { get; set; } = DefaultWidgets;

    /// <summary>One shared empty instance, so default settings compare equal.</summary>
    public static readonly IReadOnlyDictionary<string, GridCell> NoIconPositions = FrozenDictionary<string, GridCell>.Empty;

    /// <summary>
    /// The sidebar's widgets until the user changes them. Fixed ids: a note's text is kept by its widget's id, even
    /// before the settings are first saved.
    /// </summary>
    public static readonly IReadOnlyList<WidgetSettings> DefaultWidgets =
    [
        new() { Id = "profile", Kind = WidgetKind.Profile },
        new() { Id = "weather", Kind = WidgetKind.Weather },
        new() { Id = "resources", Kind = WidgetKind.Resources },
        new() { Id = "wireless", Kind = WidgetKind.Wireless },
        new() { Id = "media", Kind = WidgetKind.Media },
        new() { Id = "pictures", Kind = WidgetKind.Pictures },
        new() { Id = "notes", Kind = WidgetKind.Notes },
    ];
}
