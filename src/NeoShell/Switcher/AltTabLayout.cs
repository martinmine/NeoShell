namespace NeoShell.Switcher;

/// <summary>Where an item of the window switcher goes: its row, and its left edge and width in that row.</summary>
public readonly record struct SwitcherSlot(int Row, double X, double Width);

/// <summary>An item of the window switcher: a window, or a snap group of windows (<paramref name="Group"/>, most
/// recently used first), which comes just before the first of them.</summary>
public sealed record SwitcherEntry(nint Window, IReadOnlyList<nint>? Group = null);

/// <summary>The window switcher's order and grid, as Explorer's Alt+Tab lays them out.</summary>
public static class AltTabLayout
{
    /// <summary>
    /// The windows to switch between, most recently used first: the one in front, then the others as they're stacked
    /// (<paramref name="zOrder"/>, topmost first). Windows the z-order doesn't know go last.
    /// </summary>
    public static IReadOnlyList<nint> Order(IReadOnlyList<nint> windows, IReadOnlyList<nint> zOrder, nint foreground)
    {
        var depth = new Dictionary<nint, int>();
        for (int i = 0; i < zOrder.Count; i++)
            depth.TryAdd(zOrder[i], i);
        return [.. windows
            .OrderBy(w => w == foreground ? -1 : depth.GetValueOrDefault(w, int.MaxValue))];
    }

    /// <summary>
    /// The windows with their snap groups (Explorer's "Show my snapped windows ... when I press Alt+Tab"): each group
    /// of two or more of them just before the first of its windows, which still have items of their own.
    /// </summary>
    public static IReadOnlyList<SwitcherEntry> WithGroups(IReadOnlyList<nint> order, IReadOnlyList<IReadOnlyList<nint>> groups)
    {
        var entries = new List<SwitcherEntry>();
        var shown = new HashSet<IReadOnlyList<nint>>();
        foreach (nint window in order)
        {
            IReadOnlyList<nint>? group = groups.FirstOrDefault(g => g.Contains(window));
            if (group is not null && shown.Add(group))
            {
                List<nint> members = [.. order.Where(group.Contains)];
                if (members.Count >= 2)
                    entries.Add(new SwitcherEntry(window, members));
            }
            entries.Add(new SwitcherEntry(window));
        }
        return entries;
    }

    /// <summary>
    /// The item chosen first: the one after the window in front (the first window's own item, past its group), or the
    /// last one going backwards.
    /// </summary>
    public static int FirstSelection(IReadOnlyList<SwitcherEntry> entries, bool backwards)
    {
        if (entries.Count == 0)
            return -1;
        if (backwards)
            return entries.Count - 1;
        int front = 0;
        while (front < entries.Count - 1 && entries[front].Group is not null)
            front++;
        return Math.Min(front + 1, entries.Count - 1);
    }

    /// <summary>
    /// Rows of items (of <paramref name="widths"/>) no wider than <paramref name="maxWidth"/>, each centred in the
    /// widest, <paramref name="spacing"/> apart; returns the slots and the width of the grid.
    /// </summary>
    public static (IReadOnlyList<SwitcherSlot> Slots, double Width) Arrange(IReadOnlyList<double> widths, double maxWidth, double spacing)
    {
        var rows = new List<List<int>>();
        var rowWidths = new List<double>();
        for (int i = 0; i < widths.Count; i++)
        {
            // A new row when this one is full; an item wider than the grid gets a row to itself.
            if (rows.Count == 0 || rowWidths[^1] + spacing + widths[i] > maxWidth)
            {
                rows.Add([i]);
                rowWidths.Add(widths[i]);
                continue;
            }
            rows[^1].Add(i);
            rowWidths[^1] += spacing + widths[i];
        }

        double gridWidth = rowWidths.DefaultIfEmpty(0).Max();
        var slots = new SwitcherSlot[widths.Count];
        for (int row = 0; row < rows.Count; row++)
        {
            double x = (gridWidth - rowWidths[row]) / 2;
            foreach (int i in rows[row])
            {
                slots[i] = new SwitcherSlot(row, x, widths[i]);
                x += widths[i] + spacing;
            }
        }
        return (slots, gridWidth);
    }

    /// <summary>Tab and Shift+Tab, or the arrow keys along a row: the next or previous item, round the end.</summary>
    public static int Step(int index, int count, int delta) => count == 0 ? -1 : ((index + delta) % count + count) % count;

    /// <summary>
    /// Up and down: the item in the row above or below whose middle is nearest the current item's; the same item at
    /// the top or bottom row.
    /// </summary>
    public static int Vertical(IReadOnlyList<SwitcherSlot> slots, int index, int delta)
    {
        if (index < 0 || index >= slots.Count)
            return index;

        int row = slots[index].Row + delta;
        double middle = slots[index].X + slots[index].Width / 2;
        int best = index;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            double distance = Math.Abs(slots[i].X + slots[i].Width / 2 - middle);
            if (slots[i].Row == row && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }
}
