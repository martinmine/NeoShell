using Windows.Graphics;

namespace NeoShell.Tray;

/// <summary>Where a dragged tray icon would go: on or beside an icon, or onto the chevron.</summary>
/// <param name="Index">The icon it's dropped on, in its row (the taskbar's or the overflow's); -1: the chevron.</param>
/// <param name="After">Dropped on the icon's right half: it goes after it, otherwise before it.</param>
public readonly record struct TrayDropTarget(bool InOverflow, int Index, bool After)
{
    /// <summary>Onto the chevron: into the overflow, first.</summary>
    public static readonly TrayDropTarget Chevron = new(true, -1, false);

    public bool IsChevron => Index < 0;
}

/// <summary>The caption shown above a dragged tray icon.</summary>
public enum TrayDragCaption { None, Pin, Unpin, CantDrop }

/// <summary>
/// The order of tray icons and moving them by dragging, as Explorer's taskbar does it (Taskbar.dll's
/// <c>NotifyIconSettingsDatabase</c>, SystemTray.dll's <c>DragDropManager</c>). One order holds every icon, those on the
/// taskbar and those in the overflow alike (<c>UIOrderList</c>); each row shows its own icons in that order.
/// </summary>
public static class TrayIconOrder
{
    /// <summary>Sorts icons by their place in the order; an icon missing from it goes first, as in Explorer.</summary>
    public static List<T> Sort<T>(IEnumerable<T> icons, Func<T, ulong?> id, IReadOnlyList<ulong> order)
    {
        var index = new Dictionary<ulong, int>();
        for (int i = 0; i < order.Count; i++)
            index.TryAdd(order[i], i);
        // OrderBy is stable: icons in the same place keep the order they were added in.
        return [.. icons.OrderBy(icon => id(icon) is { } key && index.TryGetValue(key, out int i) ? i : 0)];
    }

    /// <summary>
    /// Moves an icon before or after another in the order; when the other isn't in it, first.
    /// </summary>
    public static List<ulong> Move(IReadOnlyList<ulong> order, ulong id, ulong target, bool after)
    {
        List<ulong> moved = [.. order];
        if (id == target)
            return moved;
        moved.Remove(id);
        int index = moved.IndexOf(target);
        moved.Insert(index < 0 ? 0 : after ? index + 1 : index, id);
        return moved;
    }

    /// <summary>
    /// What a drag is over: an icon in either row (the overflow's only while it's open), else the chevron, else nothing.
    /// On an icon, its left half puts the dragged one before it and its right half after it.
    /// </summary>
    public static TrayDropTarget? Find(PointInt32 point, IReadOnlyList<RectInt32> promoted, IReadOnlyList<RectInt32>? overflow, RectInt32? chevron)
    {
        if (overflow is not null && Find(point, overflow) is { } inOverflow)
            return new TrayDropTarget(true, inOverflow.Index, inOverflow.After);
        if (Find(point, promoted) is { } onTaskbar)
            return new TrayDropTarget(false, onTaskbar.Index, onTaskbar.After);
        return chevron is { } rect && Contains(rect, point) ? TrayDropTarget.Chevron : null;
    }

    /// <summary>
    /// The caption over the drag: a pin over the taskbar's row for an icon from the overflow, an unpin over the overflow
    /// or the chevron for one from the taskbar, none within its own row, and "can't drop here" anywhere else.
    /// </summary>
    public static TrayDragCaption Caption(bool fromOverflow, TrayDropTarget? target) => target switch
    {
        null => TrayDragCaption.CantDrop,
        { InOverflow: var inOverflow } when inOverflow == fromOverflow => TrayDragCaption.None,
        { InOverflow: true } => TrayDragCaption.Unpin,
        _ => TrayDragCaption.Pin,
    };

    private static (int Index, bool After)? Find(PointInt32 point, IReadOnlyList<RectInt32> icons)
    {
        for (int i = 0; i < icons.Count; i++)
        {
            RectInt32 rect = icons[i];
            if (Contains(rect, point))
                return (i, point.X > rect.X + rect.Width / 2);
        }
        return null;
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
}
