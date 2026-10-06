using NeoShell.Settings;

namespace NeoShell.Desktop;

/// <summary>
/// Where the desktop's icons go: cells of a grid, filled column by column from the top left as on Explorer's desktop,
/// except where the user has put an icon.
/// </summary>
public static class DesktopGrid
{
    /// <summary>
    /// Each icon's cell. An icon goes where it's wanted when that's on the grid and no icon before it took the cell,
    /// else to the nearest free cell; icons wanted nowhere fill the first free cells, column by column. Columns
    /// aren't limited: icons past the screen's width scroll.
    /// </summary>
    /// <param name="wanted">Where each icon wants to be, or null.</param>
    /// <param name="rows">How many icons fit in a column.</param>
    public static GridCell[] Arrange(IReadOnlyList<GridCell?> wanted, int rows)
    {
        rows = Math.Max(1, rows);
        var cells = new GridCell?[wanted.Count];
        var taken = new HashSet<GridCell>();
        for (int i = 0; i < wanted.Count; i++)
        {
            if (wanted[i] is { } cell && cell.Column >= 0 && cell.Row >= 0 && cell.Row < rows && taken.Add(cell))
                cells[i] = cell;
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (cells[i] is not null || wanted[i] is not { } cell)
                continue;
            GridCell nearest = Nearest(Clamp(cell, rows, int.MaxValue), taken, rows, int.MaxValue);
            cells[i] = nearest;
            taken.Add(nearest);
        }

        int next = 0;
        for (int i = 0; i < wanted.Count; i++)
        {
            if (cells[i] is not null)
                continue;
            GridCell cell;
            do
                cell = new GridCell(next / rows, next++ % rows);
            while (!taken.Add(cell));
            cells[i] = cell;
        }
        return [.. cells.Select(cell => cell!.Value)];
    }

    /// <summary>
    /// Moves icons to new cells, as when they're dragged across the desktop or dropped on it: each goes to its target,
    /// kept within the grid on screen, or to the free cell nearest to it when another icon is there.
    /// </summary>
    /// <param name="cells">Every icon's cell now.</param>
    /// <param name="moves">The icons that move, by index into <paramref name="cells"/>, and where to.</param>
    /// <param name="rows">How many icons fit in a column.</param>
    /// <param name="columns">How many columns fit on the screen.</param>
    public static GridCell[] Move(IReadOnlyList<GridCell> cells, IReadOnlyList<(int Index, GridCell Target)> moves, int rows, int columns)
    {
        rows = Math.Max(1, rows);
        columns = Math.Max(1, columns);
        GridCell[] result = [.. cells];
        var moving = moves.Select(move => move.Index).ToHashSet();
        var taken = cells.Where((_, i) => !moving.Contains(i)).ToHashSet();
        foreach ((int index, GridCell target) in moves)
        {
            // A full screen leaves only the columns past it.
            int limit = taken.Count >= (long)rows * columns ? int.MaxValue : columns;
            GridCell cell = Clamp(target, rows, limit);
            result[index] = taken.Contains(cell) ? Nearest(cell, taken, rows, limit) : cell;
            taken.Add(result[index]);
        }
        return result;
    }

    private static GridCell Clamp(GridCell cell, int rows, int columns) =>
        new(Math.Clamp(cell.Column, 0, columns - 1), Math.Clamp(cell.Row, 0, rows - 1));

    /// <summary>The free cell nearest to a cell, in growing squares around it; the closer, then the leftmost, wins.</summary>
    private static GridCell Nearest(GridCell from, HashSet<GridCell> taken, int rows, int columns)
    {
        if (!taken.Contains(from))
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
                    if (!onRing || column < 0 || column >= columns || row < 0 || row >= rows || taken.Contains(cell))
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
