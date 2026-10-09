namespace NeoShell.StartMenu;

/// <summary>The keys that move the focus between Start's items.</summary>
public enum StartMove { Left, Right, Up, Down, Home, End }

/// <summary>
/// How the arrow keys, Home and End move the focus in All (and in an open category or folder), as in Explorer's 25H2
/// Start, whose All is a grouped GridView: the letters' headers take the focus too. Left and Right step through
/// headers and items in order; Up and Down move by rows within a letter's items, from its first row up to its header
/// and from its last row down to the next header, and stay put where there's nothing straight above or below; Home and
/// End go to the first header and the last item. Without headers (category cards, a panel's apps) it's a plain grid.
/// </summary>
public static class StartNavigation
{
    /// <summary>A place to focus: a letter's header (<see cref="Index"/> -1) or one of its items.</summary>
    public readonly record struct Spot(int Group, int Index)
    {
        public bool IsHeader => Index < 0;
    }

    /// <summary>
    /// Where the focus goes from <paramref name="from"/>; null where it stays. <paramref name="sizes"/> are the
    /// groups' item counts, <paramref name="columns"/> items to a row; <paramref name="column"/> is the column the
    /// focus came down or up in, for leaving a header upwards.
    /// </summary>
    public static Spot? Move(IReadOnlyList<int> sizes, bool headers, int columns, Spot from, StartMove key, int column = 0)
    {
        columns = Math.Max(1, columns);
        List<Spot> order = [];
        for (int group = 0; group < sizes.Count; group++)
        {
            if (headers)
                order.Add(new Spot(group, -1));
            for (int index = 0; index < sizes[group]; index++)
                order.Add(new Spot(group, index));
        }
        if (order.Count == 0)
            return null;

        int at = order.IndexOf(from);
        switch (key)
        {
            case StartMove.Home:
                return order[0];
            case StartMove.End:
                return order[^1];
            case StartMove.Left:
                return at > 0 ? order[at - 1] : null;
            case StartMove.Right:
                return at >= 0 && at < order.Count - 1 ? order[at + 1] : null;
        }
        if (at < 0)
            return null;

        bool down = key == StartMove.Down;
        if (from.IsHeader)
        {
            if (down)
                return sizes[from.Group] > 0 ? new Spot(from.Group, 0) : NextHeader(sizes, from.Group);
            // Up from a header: the last row of the letter above, in the column the focus came from.
            int above = from.Group - 1;
            while (above >= 0 && sizes[above] == 0)
                above--;
            if (above < 0)
                return null;
            int lastRow = (sizes[above] - 1) / columns;
            return new Spot(above, Math.Min(lastRow * columns + Math.Min(column, columns - 1), sizes[above] - 1));
        }

        int row = from.Index / columns, col = from.Index % columns, rows = (sizes[from.Group] + columns - 1) / columns;
        if (down)
        {
            if (row < rows - 1)
                return row * columns + columns + col < sizes[from.Group] ? new Spot(from.Group, from.Index + columns) : null;
            return headers ? NextHeader(sizes, from.Group) : null;
        }
        if (row > 0)
            return new Spot(from.Group, from.Index - columns);
        return headers ? new Spot(from.Group, -1) : null;
    }

    private static Spot? NextHeader(IReadOnlyList<int> sizes, int group) => group + 1 < sizes.Count ? new Spot(group + 1, -1) : null;
}
