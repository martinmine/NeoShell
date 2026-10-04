namespace NeoShell.Settings;

public enum TaskbarAlignment { Center, Left }

public enum CombineButtons { Always, WhenFull, Never }

public enum TrayMode { ShowAll, Overflow }

public enum Backdrop { Acrylic, Mica, Translucent, Transparent }

public enum DesktopSortOrder { Name, Size, ItemType, DateModified }

/// <summary>A pinned app: launched through its AppUserModelID when it has one, otherwise through its path.</summary>
public sealed record PinnedApp(string DisplayName, string? AppUserModelId = null, string? Path = null, string? Arguments = null);

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
    public bool ShowSearchButton { get; set; } = true;
    public TrayMode TrayMode { get; set; } = TrayMode.Overflow;
    public Backdrop TaskbarBackdrop { get; set; } = Backdrop.Acrylic;
    public IReadOnlyList<PinnedApp> PinnedTaskbarApps { get; set; } = [];
    public IReadOnlyList<PinnedApp> PinnedStartApps { get; set; } = [];
    /// <summary>Whether Explorer's Start pins have been added to <see cref="PinnedStartApps"/>; done once.</summary>
    public bool ExplorerStartPinsImported { get; set; }
    /// <summary>Whether Explorer's taskbar pins have been added to <see cref="PinnedTaskbarApps"/>; done once.</summary>
    public bool ExplorerTaskbarPinsImported { get; set; }
    /// <summary>Start's size in effective pixels, as the user left it by dragging a corner.</summary>
    public double StartMenuWidth { get; set; } = 832;
    public double StartMenuHeight { get; set; } = 860;
    /// <summary>The calendar under the notification center folded away to its heading, as the user left it.</summary>
    public bool CalendarCollapsed { get; set; }
    /// <summary>The length of a focus session, as last chosen in the calendar.</summary>
    public int FocusMinutes { get; set; } = 30;
    public DesktopSortOrder DesktopSortOrder { get; set; } = DesktopSortOrder.Name;
}
