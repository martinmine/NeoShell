namespace NeoShell.Desktop;

/// <summary>A cell of one monitor's icon grid: columns from the left, rows from the top.</summary>
public readonly record struct GridCell(int Column, int Row);

/// <summary>How many cells fit across and down one monitor's work area.</summary>
public readonly record struct GridSize(int Columns, int Rows);

/// <summary>Where an icon is: a cell on the grid of one monitor (its index in work-area order).</summary>
public readonly record struct IconPlace(int Workspace, GridCell Cell);

/// <summary>
/// Where the desktop's icons go: cells of each monitor's grid, filled column by column from the top left as on
/// Explorer's desktop, the primary monitor first, except where the user has put an icon.
/// </summary>
public static class DesktopGrid
{
    /// <summary>
    /// Each icon's place. An icon goes where it's wanted when that's on its monitor's grid and no icon before it took
    /// the cell, else to the nearest free cell on that monitor; icons wanted nowhere (new ones, or ones from a monitor
    /// that's gone) fill the first free cells of the primary monitor, column by column, then the other monitors'.
    /// When every grid is full, the primary monitor's columns go on past the screen (they scroll).
    /// </summary>
    /// <param name="wanted">Where each icon wants to be, or null.</param>
    /// <param name="primary">The primary monitor's index in <paramref name="grids"/>.</param>
    public static IconPlace[] Arrange(IReadOnlyList<IconPlace?> wanted, IReadOnlyList<GridSize> grids, int primary)
    {
        var places = new IconPlace?[wanted.Count];
        var taken = new HashSet<IconPlace>();
        for (int i = 0; i < wanted.Count; i++)
        {
            if (wanted[i] is { } place && IsOnGrid(place, grids) && taken.Add(place))
                places[i] = place;
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (places[i] is not null || wanted[i] is not { } place || place.Workspace < 0 || place.Workspace >= grids.Count)
                continue;
            GridSize grid = Fix(grids[place.Workspace]);
            if (Count(taken, place.Workspace) >= grid.Columns * (long)grid.Rows)
                continue; // a full monitor: placed with the new icons
            var nearest = new IconPlace(place.Workspace, Nearest(Clamp(place.Cell, grid.Rows, grid.Columns), taken, place.Workspace, grid.Rows, grid.Columns));
            places[i] = nearest;
            taken.Add(nearest);
        }

        IEnumerable<IconPlace> free = FreePlaces(grids, primary, taken);
        using IEnumerator<IconPlace> next = free.GetEnumerator();
        for (int i = 0; i < wanted.Count; i++)
        {
            if (places[i] is not null)
                continue;
            next.MoveNext();
            places[i] = next.Current;
            taken.Add(next.Current);
        }
        return [.. places.Select(place => place!.Value)];
    }

    /// <summary>
    /// Packs the icons in their order, each on the monitor it's on, column by column from the top left: "Auto arrange
    /// icons" and Sort by, which in Explorer keep each icon on its monitor. Icons of a monitor that's gone, or that
    /// don't fit on theirs, join the primary monitor's.
    /// </summary>
    /// <param name="workspaces">The monitor each icon is on, or null for none yet.</param>
    public static IconPlace[] Pack(IReadOnlyList<int?> workspaces, IReadOnlyList<GridSize> grids, int primary)
    {
        var used = new int[grids.Count];
        var places = new IconPlace?[workspaces.Count];
        for (int i = 0; i < workspaces.Count; i++)
        {
            if (workspaces[i] is not { } workspace || workspace < 0 || workspace >= grids.Count || workspace == primary)
                continue;
            GridSize grid = Fix(grids[workspace]);
            if (used[workspace] < grid.Columns * (long)grid.Rows)
            {
                places[i] = new IconPlace(workspace, CellAt(used[workspace], grid.Rows));
                used[workspace]++;
            }
        }
        int rows = Fix(grids[primary]).Rows;
        for (int i = 0; i < workspaces.Count; i++)
        {
            if (places[i] is null)
                places[i] = new IconPlace(primary, CellAt(used[primary]++, rows));
        }
        return [.. places.Select(place => place!.Value)];
    }

    /// <summary>
    /// Moves icons to new places, as when they're dragged across the desktop or dropped on it: each goes to its target,
    /// kept within its monitor's grid, or to the free cell nearest to it when another icon is there.
    /// </summary>
    /// <param name="places">Every icon's place now.</param>
    /// <param name="moves">The icons that move, by index into <paramref name="places"/>, and where to.</param>
    public static IconPlace[] Move(IReadOnlyList<IconPlace> places, IReadOnlyList<(int Index, IconPlace Target)> moves, IReadOnlyList<GridSize> grids)
    {
        IconPlace[] result = [.. places];
        var moving = moves.Select(move => move.Index).ToHashSet();
        var taken = places.Where((_, i) => !moving.Contains(i)).ToHashSet();
        foreach ((int index, IconPlace target) in moves)
        {
            if (target.Workspace < 0 || target.Workspace >= grids.Count)
            {
                taken.Add(result[index]);
                continue;
            }
            GridSize grid = Fix(grids[target.Workspace]);
            // A full monitor leaves only the columns past it.
            int limit = Count(taken, target.Workspace) >= grid.Columns * (long)grid.Rows ? int.MaxValue : grid.Columns;
            GridCell cell = Clamp(target.Cell, grid.Rows, limit);
            result[index] = new IconPlace(target.Workspace, Nearest(cell, taken, target.Workspace, grid.Rows, limit));
            taken.Add(result[index]);
        }
        return result;
    }

    /// <summary>Free cells in the order new icons take them: the primary monitor's, the others', then past the primary's.</summary>
    private static IEnumerable<IconPlace> FreePlaces(IReadOnlyList<GridSize> grids, int primary, HashSet<IconPlace> taken)
    {
        IEnumerable<int> order = Enumerable.Range(0, grids.Count).Where(index => index != primary).Prepend(primary);
        foreach (int workspace in order)
        {
            GridSize grid = Fix(grids[workspace]);
            for (int index = 0; index < grid.Columns * grid.Rows; index++)
            {
                var place = new IconPlace(workspace, CellAt(index, grid.Rows));
                if (!taken.Contains(place))
                    yield return place;
            }
        }
        int rows = Fix(grids[primary]).Rows;
        for (int index = Fix(grids[primary]).Columns * rows; ; index++)
        {
            var place = new IconPlace(primary, CellAt(index, rows));
            if (!taken.Contains(place))
                yield return place;
        }
    }

    private static bool IsOnGrid(IconPlace place, IReadOnlyList<GridSize> grids) =>
        place.Workspace >= 0 && place.Workspace < grids.Count
        && place.Cell.Column >= 0 && place.Cell.Column < Fix(grids[place.Workspace]).Columns
        && place.Cell.Row >= 0 && place.Cell.Row < Fix(grids[place.Workspace]).Rows;

    /// <summary>A work area too small for one icon still has one cell.</summary>
    private static GridSize Fix(GridSize grid) => new(Math.Max(1, grid.Columns), Math.Max(1, grid.Rows));

    private static GridCell CellAt(int index, int rows) => new(index / rows, index % rows);

    private static int Count(HashSet<IconPlace> taken, int workspace) => taken.Count(place => place.Workspace == workspace);

    private static GridCell Clamp(GridCell cell, int rows, int columns) =>
        new(Math.Clamp(cell.Column, 0, columns - 1), Math.Clamp(cell.Row, 0, rows - 1));

    /// <summary>The free cell nearest to a cell, in growing squares around it; the closer, then the leftmost, wins.</summary>
    private static GridCell Nearest(GridCell from, HashSet<IconPlace> taken, int workspace, int rows, int columns)
    {
        if (!taken.Contains(new IconPlace(workspace, from)))
            return from;

        for (int distance = 1; ; distance++)
        {
            GridCell? best = null;
            int bestSquare = int.MaxValue;
            for (int column = from.Column - distance; column <= from.Column + distance; column++)
            {
                for (int row = from.Row - distance; row <= from.Row + distance; row++)
                {
                    bool onRing = Math.Abs(column - from.Column) == distance || Math.Abs(row - from.Row) == distance;
                    var cell = new GridCell(column, row);
                    if (!onRing || column < 0 || column >= columns || row < 0 || row >= rows || taken.Contains(new IconPlace(workspace, cell)))
                        continue;
                    int square = (column - from.Column) * (column - from.Column) + (row - from.Row) * (row - from.Row);
                    if (square < bestSquare)
                        (best, bestSquare) = (cell, square);
                }
            }
            if (best is { } found)
                return found;
        }
    }
}
