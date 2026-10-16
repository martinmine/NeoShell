using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Taskbar;

namespace NeoShell.Agents;

/// <summary>What <c>set_agent_status</c> shows on the agent's window's taskbar button.</summary>
public enum AgentStatus { Working, Progress, Paused, Error, Attention, Clear }

/// <summary>
/// The tools agents show themselves in the shell with: their state on their own window's taskbar button, and
/// notifications that can ask the user something. What an agent shows goes when it leaves or is disconnected.
/// </summary>
public static class PresenceTools
{
    private const int MaxButtons = 3;
    private const int MaxWaitSeconds = 600;

    public static readonly IReadOnlyDictionary<string, AgentStatus> Statuses =
        Enum.GetValues<AgentStatus>().ToDictionary(status => AgentData.Name(status));

    internal static IReadOnlyList<AgentTool> Create(Taskbars taskbars)
    {
        var shown = new Shown(taskbars);
        return
        [
            new("set_agent_status", AgentToolGroup.Presence,
                "Shows your state on your own window's taskbar button (the terminal or app you run in, found from your " +
                "process; or the window handle given): working (a moving bar), progress (percent), paused, error, " +
                "attention (the button flashes until the window is brought to the front) or clear. It goes when you disconnect.",
                ToolArguments.Schema(new JsonObject
                {
                    ["status"] = ToolArguments.ChoiceSchema("What to show", Statuses.Keys),
                    ["percent"] = ToolArguments.IntegerSchema("For progress (and optionally paused or error): how far, 0 to 100", 0, 100),
                    ["handle"] = new JsonObject { ["type"] = "integer", ["description"] = "Another window of yours, from list_windows" },
                }, "status"),
                (arguments, session) => Task.FromResult(SetStatus(taskbars, shown, arguments, session))),
            new("notify", AgentToolGroup.Presence,
                "Shows a notification from you (named as yours) above the taskbar. With buttons it stays until the user " +
                "picks one or closes it, and the call answers with the button (null if closed or no answer in waitSeconds).",
                ToolArguments.Schema(new JsonObject
                {
                    ["title"] = ToolArguments.StringSchema("The first line, bold"),
                    ["text"] = ToolArguments.StringSchema("The text under it"),
                    ["buttons"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = $"Up to {MaxButtons} buttons' labels",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["maxItems"] = MaxButtons,
                    },
                    ["waitSeconds"] = ToolArguments.IntegerSchema($"With buttons: how long to wait for an answer (default 120)", 1, MaxWaitSeconds),
                }, "title"),
                (arguments, session) => NotifyAsync(taskbars, shown, arguments, session))
            {
                Timeout = TimeSpan.FromSeconds(MaxWaitSeconds + 10),
            },
        ];
    }

    /// <summary>
    /// The agent's own window: the first of its processes, nearest first (the bridge, the agent, the terminal it runs
    /// in...), with a window that has a taskbar button; of several, the first in <paramref name="windows"/>.
    /// </summary>
    public static WindowInfo? AgentWindow(IReadOnlyList<ProcessEntry> processes, IReadOnlyList<WindowInfo> windows)
    {
        foreach (ProcessEntry process in processes)
        {
            if (windows.FirstOrDefault(window => window.ProcessId == process.Id) is { } window)
                return window;
        }
        return null;
    }

    /// <summary>The progress a status shows; null for one that shows none (attention keeps what's there; clear takes it away).</summary>
    public static TaskProgress? ProgressFor(AgentStatus status, int? percent) => status switch
    {
        AgentStatus.Working => new TaskProgress(TaskbarProgressState.Indeterminate, 0),
        AgentStatus.Progress => new TaskProgress(TaskbarProgressState.Normal, (percent ?? 0) / 100.0),
        AgentStatus.Paused => new TaskProgress(TaskbarProgressState.Paused, (percent ?? 100) / 100.0),
        AgentStatus.Error => new TaskProgress(TaskbarProgressState.Error, (percent ?? 100) / 100.0),
        _ => null,
    };

    private static ToolResult SetStatus(Taskbars taskbars, Shown shown, JsonObject arguments, McpSession session)
    {
        AgentStatus status = ToolArguments.Choice(arguments, "status", Statuses);
        int? percent = arguments["percent"] is null ? null : ToolArguments.Integer(arguments, "percent", 0, 0, 100);
        if (status == AgentStatus.Progress && percent is null)
            throw new ToolArgumentException("progress needs percent");

        WindowTracker tracker = taskbars.Tracker;
        WindowInfo? window;
        if (arguments["handle"] is not null)
        {
            window = ToolArguments.Window(arguments, tracker.Windows, w => w.Handle);
        }
        else
        {
            // Of an app's several windows (terminals), the one used last.
            Dictionary<nint, int> used = tracker.RecentlyActive.Select((hwnd, index) => (hwnd, index)).ToDictionary(p => p.hwnd, p => p.index);
            window = AgentWindow(session.Processes, [.. tracker.Windows.OrderBy(w => used.GetValueOrDefault(w.Handle, int.MaxValue))]);
        }
        if (window is null)
        {
            return ToolResult.Error("NeoShell found no taskbar window for your processes (" +
                string.Join(" < ", session.Processes.Select(p => p.Name)) + "); give a window's handle from list_windows");
        }

        if (status == AgentStatus.Attention)
            tracker.FlashForAgent(window.Handle);
        else
            tracker.SetAgentProgress(window.Handle, ProgressFor(status, percent));
        shown.Status(session, window.Handle, status != AgentStatus.Clear);
        return ToolResult.FromData(new JsonObject
        {
            ["window"] = (long)window.Handle,
            ["title"] = window.Title,
            ["status"] = AgentData.Name(status),
        });
    }

    private static async Task<ToolResult> NotifyAsync(Taskbars taskbars, Shown shown, JsonObject arguments, McpSession session)
    {
        string title = ToolArguments.RequiredString(arguments, "title");
        string text = ToolArguments.OptionalString(arguments, "text") ?? "";
        IReadOnlyList<string> buttons = ToolArguments.Strings(arguments, "buttons", MaxButtons, maxLength: 40);
        int wait = ToolArguments.Integer(arguments, "waitSeconds", 120, 1, MaxWaitSeconds);
        if (taskbars.AgentToasts is not { } toasts)
            return ToolResult.Error("NeoShell's notifications haven't started yet");

        string key = $"Agent {Guid.NewGuid():N}";
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!toasts.ShowAgentToast(key, session.ClientName, title, text, buttons, button => answer.TrySetResult(button)))
            return ToolResult.Error("The notification didn't show: Do not disturb is on, agents' banners are turned off, or the notification center is open");
        if (buttons.Count == 0)
            return ToolResult.FromData(new JsonObject { ["shown"] = true });

        shown.Toast(session, key, () => answer.TrySetResult(null));
        try
        {
            string? button = await answer.Task.WaitAsync(TimeSpan.FromSeconds(wait));
            return ToolResult.FromData(new JsonObject { ["button"] = button, ["closed"] = button is null });
        }
        catch (TimeoutException)
        {
            toasts.Hide(key);
            return ToolResult.FromData(new JsonObject { ["button"] = null, ["timedOut"] = true });
        }
    }

    /// <summary>What each agent shows, taken away when it ends.</summary>
    private sealed class Shown(Taskbars taskbars)
    {
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
        // Each agent's windows with its status, and its toasts waiting for an answer with how to give up waiting.
        private readonly Dictionary<McpSession, (HashSet<nint> Windows, Dictionary<string, Action> Toasts)> _shown = [];

        public void Status(McpSession session, nint hwnd, bool on)
        {
            if (on)
                Of(session).Windows.Add(hwnd);
            else if (_shown.TryGetValue(session, out var shown))
                shown.Windows.Remove(hwnd);
        }

        public void Toast(McpSession session, string key, Action giveUp) => Of(session).Toasts[key] = giveUp;

        private (HashSet<nint> Windows, Dictionary<string, Action> Toasts) Of(McpSession session)
        {
            if (!_shown.TryGetValue(session, out var shown))
            {
                _shown[session] = shown = ([], []);
                session.Ended += () => _dispatcher.TryEnqueue(() => Clear(session));
            }
            return shown;
        }

        private void Clear(McpSession session)
        {
            if (!_shown.Remove(session, out var shown))
                return;
            foreach (nint hwnd in shown.Windows)
                taskbars.Tracker.SetAgentProgress(hwnd, null);
            foreach ((string key, Action giveUp) in shown.Toasts)
            {
                taskbars.AgentToasts?.Hide(key);
                giveUp();
            }
        }
    }
}
