using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>One taskbar button: a pinned app and/or running windows.</summary>
/// <param name="Key">Stable identity across rebuilds, so buttons are updated rather than recreated.</param>
public sealed record TaskButtonModel(string Key, PinnedApp? Pinned, IReadOnlyList<WindowInfo> Windows);

/// <summary>Turns pinned apps and running windows into the row of taskbar buttons.</summary>
public static class TaskListBuilder
{
    /// <summary>Effective width of a combined (icon-only) button, including margins.</summary>
    public const double CombinedButtonWidth = 44;

    /// <summary>Effective width of an uncombined button with its label, including margins.</summary>
    public const double LabeledButtonWidth = 164;

    /// <summary>
    /// Pinned apps first, in their pinned order, with their windows; then the other running apps in the order their
    /// first window appeared. Combined, an app is one button; otherwise each window is its own button, and a pinned
    /// app's windows take its place.
    /// </summary>
    /// <param name="windows">Windows with buttons, in the order they appeared.</param>
    public static IReadOnlyList<TaskButtonModel> Build(IReadOnlyList<PinnedApp> pinned, IReadOnlyList<WindowInfo> windows, bool combine)
    {
        var buttons = new List<TaskButtonModel>();
        var claimed = new HashSet<nint>();

        foreach (PinnedApp app in pinned)
        {
            List<WindowInfo> appWindows = [.. windows.Where(w => !claimed.Contains(w.Handle) && TaskGrouping.Matches(app, w))];
            claimed.UnionWith(appWindows.Select(w => w.Handle));
            AddButtons(TaskGrouping.Key(app), app, appWindows);
        }

        foreach (IGrouping<string, WindowInfo> group in windows
            .Where(w => !claimed.Contains(w.Handle))
            .GroupBy(TaskGrouping.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddButtons(group.Key, null, [.. group]);
        }
        return buttons;

        void AddButtons(string key, PinnedApp? app, List<WindowInfo> appWindows)
        {
            if (combine || appWindows.Count == 0)
            {
                buttons.Add(new TaskButtonModel(key, app, appWindows));
                return;
            }
            foreach (WindowInfo window in appWindows)
                buttons.Add(new TaskButtonModel($"window:{window.Handle}", app, [window]));
        }
    }

    /// <summary>Whether to combine: always, never, or when the uncombined buttons wouldn't fit.</summary>
    public static bool ShouldCombine(CombineButtons mode, IReadOnlyList<TaskButtonModel> uncombined, double availableWidth) => mode switch
    {
        CombineButtons.Never => false,
        CombineButtons.WhenFull => uncombined.Sum(Width) > availableWidth,
        _ => true,
    };

    /// <summary>Uncombined, only buttons with a window have a label; pinned apps that aren't running stay icons.</summary>
    public static double Width(TaskButtonModel button) =>
        button.Windows.Count > 0 ? LabeledButtonWidth : CombinedButtonWidth;
}
