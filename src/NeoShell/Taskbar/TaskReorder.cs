namespace NeoShell.Taskbar;

/// <summary>
/// Dragging a task button along the taskbar, as in Windows 11: the button slides along the row only, and its
/// neighbours make way as it passes them. Positions are in effective pixels along the task list.
/// </summary>
public static class TaskReorder
{
    /// <summary>How far the pointer moves before a press becomes a drag; the button then moves on from where it is.</summary>
    public const double Threshold = 6;

    /// <summary>How far the dragged button is drawn from its slot: the pointer's travel, kept within the list.</summary>
    public static double Offset(IReadOnlyList<(double Left, double Width)> slots, int dragged, double pointerTravel)
    {
        double offset = Math.Sign(pointerTravel) * Math.Max(0, Math.Abs(pointerTravel) - Threshold);
        (double left, double width) = slots[dragged];
        double min = slots[0].Left - left;
        double max = slots[^1].Left + slots[^1].Width - (left + width);
        return Math.Clamp(offset, min, max);
    }

    /// <summary>
    /// Where the button would be dropped. A neighbour makes way once the button's leading edge passes its middle,
    /// which for buttons of the same width is when the button's centre enters the neighbour's slot.
    /// </summary>
    public static int TargetIndex(IReadOnlyList<(double Left, double Width)> slots, int dragged, double offset)
    {
        double start = slots[dragged].Left + offset;
        double end = start + slots[dragged].Width;
        int target = dragged;
        for (int i = dragged + 1; i < slots.Count && end > Middle(slots[i]); i++)
            target = i;
        for (int i = dragged - 1; i >= 0 && start < Middle(slots[i]); i--)
            target = i;
        return target;
    }

    /// <summary>How far a button moves to make way: by the dragged button's width, towards the dragged one's slot.</summary>
    public static double MakeWayOffset(IReadOnlyList<(double Left, double Width)> slots, int dragged, int target, int index)
    {
        double width = slots[dragged].Width;
        if (index > dragged && index <= target)
            return -width;
        if (index < dragged && index >= target)
            return width;
        return 0;
    }

    private static double Middle((double Left, double Width) slot) => slot.Left + slot.Width / 2;
}
