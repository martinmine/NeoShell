namespace NeoShell.StartMenu;

/// <summary>
/// Dragging a pinned app around Start's grid: the icon follows the pointer, and the icons between its old and new
/// place shift one slot along to make room, as in Windows 11. Over the middle of another app or a folder, the drop
/// groups the dragged app with it instead. Slots are the cells' top-left corners, in reading order.
/// </summary>
public static class GridReorder
{
    /// <summary>How far the pointer moves before a press becomes a drag.</summary>
    public const double Threshold = 6;

    /// <summary>
    /// How far from a cell's centre, either way, the pointer groups the dragged app with the cell's. Explorer's zone is
    /// the middle three fifths or so of a 96 pixel cell; outside it, the drop goes beside the cell.
    /// </summary>
    public const double GroupZone = 28;

    /// <summary>
    /// Where a drop at <paramref name="pointer"/> goes: onto the cell under it (<c>Group</c>) when that cell may take
    /// the dragged app and the pointer is near its middle, else to a place in the grid, before or after the cell
    /// nearest the pointer, counted as the grid will be after the move.
    /// </summary>
    /// <param name="centres">The cells' centres.</param>
    /// <param name="dragged">The dragged cell, or -1 for an app coming from outside the grid.</param>
    public static (int Index, bool Group) DropTarget(
        IReadOnlyList<(double X, double Y)> centres, (double X, double Y) pointer, int dragged, Func<int, bool> canGroup)
    {
        int nearest = 0;
        double best = double.MaxValue;
        for (int i = 0; i < centres.Count; i++)
        {
            double dx = centres[i].X - pointer.X;
            double dy = centres[i].Y - pointer.Y;
            if (dx * dx + dy * dy < best)
            {
                best = dx * dx + dy * dy;
                nearest = i;
            }
        }
        if (nearest == dragged)
            return (dragged, false);

        (double x, double y) = centres[nearest];
        if (Math.Abs(pointer.X - x) <= GroupZone && Math.Abs(pointer.Y - y) <= GroupZone && canGroup(nearest))
            return (nearest, true);

        bool after = pointer.X > x;
        return dragged >= 0 && nearest > dragged
            ? (after ? nearest : nearest - 1, false)
            : (after ? nearest + 1 : nearest, false);
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
