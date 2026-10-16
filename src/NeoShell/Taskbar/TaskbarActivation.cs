using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>
/// Brings a window to the front from the taskbar (its button, a preview, a group menu) and logs it. A window clicked
/// on the taskbar sometimes didn't come all the way to the front, with no known way to make it happen: the log has what
/// was in front, whether Windows let NeoShell take the foreground, and a moment later what covers the window if it
/// isn't in front.
/// </summary>
internal static class TaskbarActivation
{
    private static readonly TimeSpan s_checkDelay = TimeSpan.FromMilliseconds(500);

    public static void BringToFront(nint hwnd, WindowTracker tracker, string source)
    {
        nint before = TopLevelWindows.GetForeground();
        bool minimized = TopLevelWindows.IsMinimized(hwnd);
        string outcome = "granted";
        if (!TopLevelWindows.Activate(hwnd))
        {
            // Windows refused though the click was NeoShell's (other input came in between): NeoShell's own key makes it
            // the source of the last input, as for Alt+Tab.
            outcome = TopLevelWindows.SwitchTo(hwnd) ? "refused, then granted after NeoShell's key" : "refused twice";
        }
        Log.Info($"{source}: {Describe(hwnd)}{(minimized ? ", minimized" : "")}; foreground was {Describe(before)}; {outcome}");
        _ = CheckInFrontAsync(hwnd, tracker, source);
    }

    private static async Task CheckInFrontAsync(nint hwnd, WindowTracker tracker, string source)
    {
        await Task.Delay(s_checkDelay);
        if (!TopLevelWindows.Exists(hwnd))
            return;

        nint foreground = TopLevelWindows.GetForeground();
        IReadOnlyList<nint> covering = TopLevelWindows.WindowsCovering(hwnd);
        // A topmost window over it is where it should be (always-on-top apps, the taskbar).
        bool inFront = (foreground == hwnd || TopLevelWindows.RootOwner(foreground) == hwnd)
            && !TopLevelWindows.IsMinimized(hwnd)
            && covering.All(TopLevelWindows.IsTopmost);
        if (inFront)
            return;

        Log.Warn($"{source}: {Describe(hwnd)} not in front after {s_checkDelay.TotalMilliseconds} ms"
            + $"{(TopLevelWindows.IsMinimized(hwnd) ? ", still minimized" : "")}; foreground {Describe(foreground)}"
            + $"; covered by [{string.Join("; ", covering.Select(Describe))}]"
            + $"; last active [{string.Join("; ", tracker.RecentlyActive.Take(4).Select(Describe))}]");
    }

    private static string Describe(nint hwnd)
    {
        if (hwnd == 0 || !TopLevelWindows.Exists(hwnd))
            return hwnd == 0 ? "none" : $"0x{hwnd:X} (gone)";

        WindowInfo window = WindowInfo.Read(hwnd);
        return $"0x{hwnd:X} {window.ClassName} \"{window.Title}\" {Path.GetFileName(window.ProcessPath) ?? $"pid {window.ProcessId}"}"
            + (TopLevelWindows.IsTopmost(hwnd) ? " topmost" : "")
            + (TopLevelWindows.IsOfHigherIntegrity(hwnd) ? " elevated" : "");
    }
}
