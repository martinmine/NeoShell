namespace NeoShell.Themes.Windows98;

/// <summary>
/// Where things go on 98's taskbar, in pixels at 96 DPI from its top left, as measured on 98 (docs/design/theme-98.md):
/// the Start button, Quick Launch and the task buttons as toolbar bands (an etched line, a gripper, the band), and the
/// notification area in its sunken panel at the right.
/// </summary>
public static class Win98TaskbarLayout
{
    public const int Height = 28;

    /// <summary>The Start button, the task buttons, Quick Launch's buttons and the notification area share these rows.</summary>
    public const int ButtonTop = 4, ButtonHeight = 22;

    public const int StartLeft = 2, StartWidth = 54;

    /// <summary>The first band's etched line, two pixels after the Start button.</summary>
    public const int FirstBand = 58;

    /// <summary>From a band's etched line: its gripper, and its contents.</summary>
    public const int GripperLeft = 4, ContentLeft = 11;

    /// <summary>A band's gripper is 18 rows, two below the buttons' top.</summary>
    public const int GripperTop = 6, GripperHeight = 18;

    /// <summary>After a band's contents, before the next band's etched line.</summary>
    public const int BandGap = 4;

    public const int QuickLaunchButton = 23;

    /// <summary>98's Quick Launch as installed: four buttons (the rest isn't shown yet).</summary>
    public const int QuickLaunchVisible = 4;

    public const int MaxButtonWidth = 160;

    public const int ButtonGap = 3;

    /// <summary>A task button's icon and label; pushed, the icon moves a pixel down and right, the label only down.</summary>
    public const int IconLeft = 4, IconTop = 3, LabelLeft = 22, LabelTop = 4, LabelRight = 4;

    /// <summary>The notification area's panel ends two pixels from the screen's right edge; its etched line is 4 before it.</summary>
    public const int TrayRight = 2, TrayEtch = 4;

    // In the panel: 3 to the first icon, 16-pixel icons side by side, 9 to the clock's text and 8 after it.
    public const int TrayIconsLeft = 3, TrayIconSpacing = 16, TrayIconTop = 3, ClockGap = 9, ClockRight = 8, ClockTop = 5;

    /// <summary>Where the task buttons begin: after Quick Launch's band when it has buttons.</summary>
    public static int TaskButtonsLeft(int quickLaunchCount) =>
        quickLaunchCount == 0
            ? FirstBand + ContentLeft
            : FirstBand + ContentLeft + Math.Min(quickLaunchCount, QuickLaunchVisible) * QuickLaunchButton + BandGap + ContentLeft;

    /// <summary>The notification area's panel width for its icons and the clock's text.</summary>
    public static int TrayWidth(int icons, int clockWidth) =>
        TrayIconsLeft + icons * TrayIconSpacing + ClockGap + clockWidth + ClockRight;

    /// <summary>
    /// The task buttons' widths in a band: 160 while they fit with their 3-pixel gaps, otherwise the band shared out,
    /// the last ones a pixel wider where it doesn't divide evenly.
    /// </summary>
    public static int[] ButtonWidths(int count, int band)
    {
        if (count == 0)
            return [];
        int room = Math.Max(0, band - (count - 1) * ButtonGap);
        if (room >= count * MaxButtonWidth)
            return [.. Enumerable.Repeat(MaxButtonWidth, count)];

        int each = room / count, wider = room % count;
        return [.. Enumerable.Range(0, count).Select(i => i < count - wider ? each : each + 1)];
    }
}
