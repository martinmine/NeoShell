using NeoShell.Interop.Windowing;
using NeoShell.Taskbar;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Where things go on XP's taskbar, in pixels at 96 DPI, as measured on XP (docs/design/theme-xp.md): the Start button
/// at the left, Quick Launch, the task buttons, and the notification area with the clock at the right.
/// </summary>
public static class XpTaskbarLayout
{
    public const int Height = 30;

    /// <summary>The Start button's art, its rounded end and shadow included; it takes clicks over all of it.</summary>
    public const int StartWidth = 100;

    /// <summary>Where Quick Launch, or the task buttons when it's empty, begin.</summary>
    public const int BandLeft = 106;

    public const int ButtonTop = 3;
    public const int ButtonHeight = 25;

    /// <summary>A task button's widest (SM_CXMINIMIZED on XP).</summary>
    public const int MaxButtonWidth = 160;

    /// <summary>
    /// How narrow buttons may get before a program's buttons are grouped: XP grouped Notepad's windows when they'd have
    /// been 108 wide, not at 122; this is a round figure in between.
    /// </summary>
    public const int MinButtonWidth = 120;

    public const int QuickLaunchButton = 23;

    /// <summary>XP's Quick Launch bar, locked as installed, was three buttons wide; more go behind its » chevron.</summary>
    public const int QuickLaunchVisible = 3;

    /// <summary>The task buttons stop this short of the notification area.</summary>
    public const int BandGap = 3;

    // The notification area: from its left edge, 11 to the first icon, 16-pixel icons 18 apart, 10 to the clock's
    // text and 16 after it.
    public const int TrayIconsLeft = 11;
    public const int TrayIconSpacing = 18;
    public const int TrayIconTop = 7;
    public const int ClockGap = 10;
    public const int ClockRight = 16;
    public const int ClockTop = 8;

    /// <summary>A task button's label and icon, from its left and top; pressed, both move one pixel down and right.</summary>
    public const int IconLeft = 10, IconTop = 5, LabelLeft = 30, LabelTop = 5, LabelRight = 13;

    /// <summary>A group's count ("10") begins a pixel left of a label, its name 4 pixels after the count.</summary>
    public const int CountLeft = 29, CountGap = 4;

    /// <summary>Quick Launch's width: its buttons, and the chevron's slot when there are more than show.</summary>
    public static int QuickLaunchWidth(int count) =>
        Math.Min(count, QuickLaunchVisible) * QuickLaunchButton + (count > QuickLaunchVisible ? QuickLaunchButton : 0);

    /// <summary>The notification area's width for its shown icons and the clock's text.</summary>
    public static int TrayWidth(int icons, int clockWidth) =>
        TrayIconsLeft + (icons > 0 ? icons * TrayIconSpacing - 2 + ClockGap : 0) + clockWidth + ClockRight;

    /// <summary>
    /// The buttons' widths in a band: as wide as XP's (160) while they fit, otherwise the band shared out, the last
    /// ones a pixel wider where it doesn't divide evenly (as XP's).
    /// </summary>
    public static int[] ButtonWidths(int count, int band)
    {
        if (count == 0)
            return [];
        if (band >= count * MaxButtonWidth)
            return [.. Enumerable.Repeat(MaxButtonWidth, count)];

        int each = Math.Max(0, band) / count, wider = Math.Max(0, band) % count;
        return [.. Enumerable.Range(0, count).Select(i => i < count - wider ? each : each + 1)];
    }

    /// <summary>
    /// XP's grouping: every window its own button, next to its program's other windows, until the taskbar fills; then
    /// the program with the most windows becomes one button, then the next, until the buttons fit at
    /// <see cref="MinButtonWidth"/> or there's nothing left to group.
    /// </summary>
    /// <param name="windows">Windows with buttons, in the order they appeared.</param>
    public static IReadOnlyList<TaskButtonModel> Group(IReadOnlyList<WindowInfo> windows, int band, bool groupSimilar)
    {
        IReadOnlyList<TaskButtonModel> programs = TaskListBuilder.Build([], windows, combine: true);
        var grouped = new HashSet<string>();
        while (true)
        {
            int count = programs.Sum(program => grouped.Contains(program.Key) ? 1 : program.Windows.Count);
            if (!groupSimilar || count * MinButtonWidth <= band)
                break;
            TaskButtonModel? most = programs
                .Where(program => !grouped.Contains(program.Key) && program.Windows.Count > 1)
                .OrderByDescending(program => program.Windows.Count)
                .FirstOrDefault();
            if (most is null)
                break;
            grouped.Add(most.Key);
        }
        return [.. programs.SelectMany(program => grouped.Contains(program.Key)
            ? [program]
            : program.Windows.Select(window => new TaskButtonModel($"window:{window.Handle}", null, [window])))];
    }

    /// <summary>
    /// A group menu's entry: the window's title without the program's name at its end ("Untitled" for "Untitled -
    /// Notepad"), as XP showed it.
    /// </summary>
    public static string GroupItemTitle(string title, string program)
    {
        string suffix = " - " + program;
        return title.Length > suffix.Length && title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? title[..^suffix.Length] : title;
    }
}
