namespace NeoShell.StartMenu;

/// <summary>
/// Dragging a pinned app around Start's grid: the icon follows the pointer, and the icons between its old and new
/// place shift one slot along to make room, as in Windows 11. Slots are the cells' top-left corners, in reading order.
/// </summary>
public static class GridReorder
{
    /// <summary>How far the pointer moves before a press becomes a drag.</summary>
    public const double Threshold = 6;

    /// <summary>Where the dragged icon would be dropped: the slot nearest to its centre.</summary>
    public static int TargetIndex(IReadOnlyList<(double X, double Y)> slots, (double X, double Y) draggedTopLeft)
    {
        int target = 0;
        double nearest = double.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            double dx = slots[i].X - draggedTopLeft.X;
            double dy = slots[i].Y - draggedTopLeft.Y;
            double distance = dx * dx + dy * dy;
            if (distance < nearest)
            {
                nearest = distance;
                target = i;
            }
        }
        return target;
    }

    /// <summary>How far the icon at <paramref name="index"/> moves to make room: to the next or previous slot.</summary>
    public static (double X, double Y) MakeWayOffset(IReadOnlyList<(double X, double Y)> slots, int dragged, int target, int index)
    {
        int to = index > dragged && index <= target ? index - 1
            : index < dragged && index >= target ? index + 1
            : index;
        return (slots[to].X - slots[index].X, slots[to].Y - slots[index].Y);
    }
}
