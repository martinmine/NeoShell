using System.Text.Json.Nodes;
using NeoShell.Interop.Network;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Power;
using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.StartMenu;
using NeoShell.Taskbar;
using Windows.Graphics;

namespace NeoShell.Agents;

/// <summary>
/// How the shell's state looks to agents: JSON with camelCase names and enum values. Texts other apps wrote (titles,
/// tooltips, notifications) go in fields of their own, never into sentences NeoShell writes.
/// </summary>
public static class AgentData
{
    /// <summary>An enum value as agents read it: <c>AlongsideExplorer</c> is <c>alongsideExplorer</c>.</summary>
    public static string Name(Enum value)
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static JsonObject Rect(RectInt32 rect) => new()
    {
        ["x"] = rect.X,
        ["y"] = rect.Y,
        ["width"] = rect.Width,
        ["height"] = rect.Height,
    };

    /// <summary>The monitors, numbered as listed: windows name theirs by that number.</summary>
    public static JsonArray Monitors(IReadOnlyList<DisplayMonitor> monitors) => new([.. monitors.Select((monitor, index) => (JsonNode)new JsonObject
    {
        ["index"] = index,
        ["primary"] = monitor.IsPrimary,
        ["bounds"] = Rect(monitor.Bounds),
        ["workArea"] = Rect(monitor.WorkArea),
        ["scalePercent"] = (int)Math.Round(monitor.Dpi * 100 / 96.0),
    })]);

    public static int? MonitorIndex(IReadOnlyList<DisplayMonitor> monitors, nint handle)
    {
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Handle == handle)
                return i;
        }
        return null;
    }

    /// <summary>A window with a taskbar button.</summary>
    /// <param name="handle">The window's handle as a number, which tools that act on windows take.</param>
    public static JsonObject Window(WindowInfo window, string appName, bool foreground, bool minimized, bool maximized,
        RectInt32 bounds, int? monitor, bool flashing, TaskProgress? progress, AppBadge? badge) => new()
    {
        ["handle"] = (long)window.Handle,
        ["title"] = window.Title,
        ["app"] = appName,
        ["appUserModelId"] = window.AppUserModelId,
        ["process"] = window.ProcessPath is { } path ? Path.GetFileName(path) : null,
        ["processId"] = window.ProcessId,
        ["foreground"] = foreground,
        ["state"] = minimized ? "minimized" : maximized ? "maximized" : "normal",
        ["bounds"] = Rect(bounds),
        ["monitor"] = monitor,
        ["flashing"] = flashing,
        ["progress"] = progress is { State: not TaskbarProgressState.None } p
            ? new JsonObject { ["state"] = Name(p.State), ["percent"] = (int)Math.Round(p.Value * 100) }
            : null,
        ["badge"] = badge is null ? null : badge.Number > 0 ? badge.Number.ToString() : Name(badge.Glyph),
    };

    public static JsonObject Network(NetworkState network) => new()
    {
        ["kind"] = Name(network.Kind),
        ["name"] = network.Name,
        ["internet"] = network.HasInternet,
        ["signalBars"] = network.Kind == NetworkKind.WiFi ? network.SignalBars : null,
    };

    public static JsonObject? Battery(BatteryState? battery) =>
        battery is null ? null : new JsonObject { ["percent"] = battery.Percent, ["charging"] = battery.IsCharging };

    public static JsonObject Notification(ToastInfo toast) => new()
    {
        ["app"] = toast.AppName,
        ["appId"] = toast.AppId,
        ["time"] = toast.Time.ToString("o"),
        ["title"] = toast.Title,
        ["body"] = toast.Body,
    };

    /// <summary>The newest notifications first, at most <paramref name="limit"/>.</summary>
    public static JsonArray Notifications(IEnumerable<ToastInfo> toasts, int limit) =>
        new([.. toasts.OrderByDescending(toast => toast.Time).Take(limit).Select(toast => (JsonNode)Notification(toast))]);

    /// <summary>Apps ranked as Start ranks them, at most <paramref name="limit"/>.</summary>
    public static JsonArray Apps(IEnumerable<AppCatalogEntry> apps, string query, int limit) =>
        new([.. AppSearch.Rank(apps, app => app.Name, query).Take(limit).Select(app => (JsonNode)new JsonObject
        {
            ["name"] = app.Name,
            ["id"] = app.Id,
        })]);

    /// <summary>Files from the index, each in Start's group (documents, folders, other).</summary>
    public static JsonArray Files(IEnumerable<IndexResult> files) => new([.. files.Select(file => (JsonNode)new JsonObject
    {
        ["name"] = file.Name,
        ["path"] = file.Path,
        ["kind"] = StartCatalog.GroupFor(file).ToLowerInvariant(),
    })]);
}
