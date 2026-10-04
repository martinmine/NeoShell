namespace NeoShell.Settings;

public enum TaskbarAlignment { Center, Left }

public enum CombineButtons { Always, WhenFull, Never }

public enum TrayMode { ShowAll, Overflow }

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
    public TrayMode TrayMode { get; set; } = TrayMode.Overflow;
    public IReadOnlyList<PinnedApp> PinnedTaskbarApps { get; set; } = [];
    public IReadOnlyList<PinnedApp> PinnedStartApps { get; set; } = [];
    public DesktopSortOrder DesktopSortOrder { get; set; } = DesktopSortOrder.Name;
}
