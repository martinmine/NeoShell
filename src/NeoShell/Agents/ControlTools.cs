using System.Text.Json.Nodes;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.QuickSettings;
using NeoShell.Settings;
using NeoShell.Snap;
using NeoShell.StartMenu;
using NeoShell.Taskbar;

namespace NeoShell.Agents;

/// <summary>The shell surfaces <c>show_surface</c> opens.</summary>
public enum AgentSurface { Start, QuickSettings, NotificationCenter, Settings }

/// <summary>
/// The tools that act in the shell: each does what a click on the taskbar, a thumbnail or Start would, on windows
/// with taskbar buttons and apps in Start's catalog only.
/// </summary>
public static class ControlTools
{
    /// <summary><c>arrange_window</c>'s positions; restore is <see cref="SnapPosition.None"/>.</summary>
    public static readonly IReadOnlyDictionary<string, SnapPosition> Arrangements = new Dictionary<string, SnapPosition>
    {
        ["minimize"] = SnapPosition.Minimized,
        ["maximize"] = SnapPosition.Maximized,
        ["restore"] = SnapPosition.None,
        ["left"] = SnapPosition.Left,
        ["right"] = SnapPosition.Right,
        ["topLeft"] = SnapPosition.TopLeft,
        ["topRight"] = SnapPosition.TopRight,
        ["bottomLeft"] = SnapPosition.BottomLeft,
        ["bottomRight"] = SnapPosition.BottomRight,
    };

    public static readonly IReadOnlyDictionary<string, AgentSurface> Surfaces =
        Enum.GetValues<AgentSurface>().ToDictionary(surface => AgentData.Name(surface));

    private static readonly IReadOnlyDictionary<string, SettingsPage> s_settingsPages =
        Enum.GetValues<SettingsPage>().ToDictionary(page => AgentData.Name(page));

    internal static IReadOnlyList<AgentTool> Create(Taskbars taskbars) =>
    [
        new("activate_window", AgentToolGroup.Control,
            "Brings a window to the front, restoring it if minimized, as a click on its taskbar button does.",
            ToolArguments.Schema(new JsonObject { ["handle"] = ToolArguments.HandleSchema() }, "handle"),
            arguments => Activate(taskbars, arguments)),
        new("arrange_window", AgentToolGroup.Control,
            "Minimizes, maximizes or restores a window, or snaps it to a half or quarter of a monitor's work area. " +
            "As the shell this is Snap's (animated, remembered for restoring); alongside Explorer the window is moved there.",
            ToolArguments.Schema(new JsonObject
            {
                ["handle"] = ToolArguments.HandleSchema(),
                ["position"] = ToolArguments.ChoiceSchema("Where the window goes", Arrangements.Keys),
                ["monitor"] = ToolArguments.IntegerSchema("A monitor's index from shell_state (default: the window's own)", 0, 15),
            }, "handle", "position"),
            arguments => ArrangeAsync(taskbars, arguments)),
        new("close_window", AgentToolGroup.Control,
            "Asks a window to close, as its close button does; the app may still ask to save first.",
            ToolArguments.Schema(new JsonObject { ["handle"] = ToolArguments.HandleSchema() }, "handle"),
            arguments => Close(taskbars, arguments)),
        new("launch", AgentToolGroup.Control,
            "Starts an app from Start's list of apps, by the id search gives (or its exact name), as a click in Start " +
            "does. Apps only, without arguments.",
            ToolArguments.Schema(new JsonObject { ["app"] = ToolArguments.StringSchema("The app's id from search, or its exact name") }, "app"),
            Launch),
        new("show_surface", AgentToolGroup.Control,
            "Opens Start, Quick Settings, the notification center or NeoShell's Settings window (on a page).",
            ToolArguments.Schema(new JsonObject
            {
                ["surface"] = ToolArguments.ChoiceSchema("What to open", Surfaces.Keys),
                ["page"] = ToolArguments.ChoiceSchema("For settings: the page", s_settingsPages.Keys),
            }, "surface"),
            arguments => ShowSurface(taskbars, arguments)),
        new("set_volume", AgentToolGroup.Control,
            "Sets the default speakers' volume and mute, as Quick Settings' slider and mute button do.",
            ToolArguments.Schema(new JsonObject
            {
                ["percent"] = ToolArguments.IntegerSchema("The volume, 0 to 100", 0, 100),
                ["muted"] = ToolArguments.BooleanSchema("Mute or unmute"),
            }),
            arguments => SetVolume(taskbars, arguments)),
        new("set_do_not_disturb", AgentToolGroup.Control,
            "Turns Do not disturb on or off, as the notification center's bell does.",
            ToolArguments.Schema(new JsonObject { ["on"] = ToolArguments.BooleanSchema("On or off") }, "on"),
            arguments => SetDoNotDisturb(taskbars, arguments)),
    ];

    private static WindowInfo WindowFrom(Taskbars taskbars, JsonObject arguments) =>
        ToolArguments.Window(arguments, taskbars.Tracker.Windows, window => window.Handle);

    private static JsonObject WindowState(nint hwnd) => new()
    {
        ["handle"] = (long)hwnd,
        ["state"] = TopLevelWindows.IsMinimized(hwnd) ? "minimized" : TopLevelWindows.IsMaximized(hwnd) ? "maximized" : "normal",
        ["bounds"] = AgentData.Rect(TopLevelWindows.GetVisibleBounds(hwnd)),
        ["foreground"] = TopLevelWindows.GetForeground() == hwnd,
    };

    private static ToolResult Activate(Taskbars taskbars, JsonObject arguments)
    {
        WindowInfo window = WindowFrom(taskbars, arguments);
        TaskbarActivation.BringToFront(window.Handle, taskbars.Tracker, "Agent");
        return ToolResult.FromData(WindowState(window.Handle));
    }

    private static async Task<ToolResult> ArrangeAsync(Taskbars taskbars, JsonObject arguments)
    {
        WindowInfo window = WindowFrom(taskbars, arguments);
        SnapPosition position = ToolArguments.Choice(arguments, "position", Arrangements);
        IReadOnlyList<DisplayMonitor> monitors = taskbars.RunMode == RunMode.Shell ? ShellWorkArea.Monitors() : DisplayMonitor.GetAll();
        int? monitorIndex = arguments["monitor"] is null ? null : ToolArguments.Integer(arguments, "monitor", 0, 0, monitors.Count - 1);
        nint hwnd = window.Handle;
        DisplayMonitor? own = monitors.FirstOrDefault(monitor => monitor.Handle == TopLevelWindows.NearestMonitorOf(hwnd));
        DisplayMonitor target = monitorIndex is { } index ? monitors[index] : own ?? monitors[0];

        switch (position)
        {
            case SnapPosition.Minimized:
                TopLevelWindows.Minimize(hwnd);
                break;
            case SnapPosition.None:
                TopLevelWindows.RestoreNow(hwnd);
                break;
            default:
                if (taskbars.Snapping is { } snapping)
                {
                    if (!snapping.Arrange(hwnd, position, target.Handle))
                        return ToolResult.Error("Snap can't move this window: it can't be resized, or it runs as administrator");
                }
                else if (TopLevelWindows.IsOfHigherIntegrity(hwnd))
                {
                    return ToolResult.Error("The window runs as administrator; NeoShell can't move it alongside Explorer");
                }
                else if (position == SnapPosition.Maximized)
                {
                    if (target.Handle != own?.Handle)
                        TopLevelWindows.Place(hwnd, target.WorkArea);
                    TopLevelWindows.Maximize(hwnd);
                }
                else if (!TopLevelWindows.CanResize(hwnd))
                {
                    return ToolResult.Error("The window can't be resized, so it can't fill a half or quarter");
                }
                else
                {
                    TopLevelWindows.Place(hwnd, SnapLayouts.Bounds(WindowSnap.Zone(position)!, target.WorkArea));
                }
                break;
        }
        // Minimizing and restoring are posted to the window: what it says it is comes a moment later.
        for (int waited = 0; waited < 1000 && !IsIn(hwnd, position); waited += 50)
            await Task.Delay(50);
        return ToolResult.FromData(WindowState(hwnd));
    }

    private static bool IsIn(nint hwnd, SnapPosition position) => position switch
    {
        SnapPosition.Minimized => TopLevelWindows.IsMinimized(hwnd),
        SnapPosition.Maximized => TopLevelWindows.IsMaximized(hwnd),
        _ => !TopLevelWindows.IsMinimized(hwnd) && !TopLevelWindows.IsMaximized(hwnd),
    };

    private static ToolResult Close(Taskbars taskbars, JsonObject arguments)
    {
        WindowInfo window = WindowFrom(taskbars, arguments);
        TopLevelWindows.Close(window.Handle);
        return new ToolResult($"Asked \"{window.Title}\" to close; the app may ask to save first, so check list_windows.");
    }

    private static async Task<ToolResult> Launch(JsonObject arguments)
    {
        string app = ToolArguments.RequiredString(arguments, "app");
        AppCatalogEntry entry = FindApp(await Task.Run(AppCatalog.Load), app);
        Launcher.Launch(StartCatalog.ToPinnedApp(entry));
        return new ToolResult($"Starting {entry.Name} ({entry.Id}); its window shows in list_windows once it opens.");
    }

    /// <summary>The app with this id, or failing that the only one with this name (both ignoring case).</summary>
    public static AppCatalogEntry FindApp(IReadOnlyList<AppCatalogEntry> apps, string app)
    {
        if (apps.FirstOrDefault(entry => entry.Id.Equals(app, StringComparison.OrdinalIgnoreCase)) is { } byId)
            return byId;
        List<AppCatalogEntry> byName = [.. apps.Where(entry => entry.Name.Equals(app, StringComparison.CurrentCultureIgnoreCase))];
        return byName.Count switch
        {
            1 => byName[0],
            0 => throw new ToolArgumentException($"Start has no app with the id or name {app}; search gives apps and their ids"),
            _ => throw new ToolArgumentException($"Several apps are called {app}; give one's id: {string.Join(", ", byName.Select(entry => entry.Id))}"),
        };
    }

    private static ToolResult ShowSurface(Taskbars taskbars, JsonObject arguments)
    {
        AgentSurface surface = ToolArguments.Choice(arguments, "surface", Surfaces);
        switch (surface)
        {
            case AgentSurface.Start:
                taskbars.OpenStartMenu();
                break;
            case AgentSurface.QuickSettings:
                taskbars.ShowQuickSettings(QuickSettingsPage.Main);
                break;
            case AgentSurface.NotificationCenter:
                if (!taskbars.IsClockFlyoutOpen)
                    taskbars.ToggleNotificationCenter();
                break;
            case AgentSurface.Settings:
                taskbars.OpenSettings(arguments["page"] is null ? null : (SettingsPage?)ToolArguments.Choice(arguments, "page", s_settingsPages));
                break;
        }
        return new ToolResult($"Opened {AgentData.Name(surface)}.");
    }

    private static ToolResult SetVolume(Taskbars taskbars, JsonObject arguments)
    {
        int? percent = arguments["percent"] is null ? null : ToolArguments.Integer(arguments, "percent", 0, 0, 100);
        bool? muted = ToolArguments.OptionalBoolean(arguments, "muted");
        if (percent is null && muted is null)
            throw new ToolArgumentException("Give percent, muted or both");
        if (taskbars.Indicators is not { HasAudioDevice: true } indicators)
            return ToolResult.Error("There are no speakers to set");
        if (percent is { } level)
            indicators.Volume = level / 100f;
        if (muted is { } mute)
            indicators.IsMuted = mute;
        return ToolResult.FromData(new JsonObject
        {
            ["device"] = indicators.AudioDeviceName,
            ["volumePercent"] = (int)Math.Round(indicators.Volume * 100),
            ["muted"] = indicators.IsMuted,
        });
    }

    private static ToolResult SetDoNotDisturb(Taskbars taskbars, JsonObject arguments)
    {
        bool on = ToolArguments.RequiredBoolean(arguments, "on");
        if (taskbars.Notifications is not { } center)
            return ToolResult.Error("NeoShell's notification center hasn't started yet");
        center.SetDoNotDisturb(on);
        return ToolResult.FromData(new JsonObject { ["doNotDisturb"] = center.DoNotDisturb });
    }
}
