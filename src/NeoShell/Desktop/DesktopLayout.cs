using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>
/// One monitor's part of the desktop as Explorer's icon layout engine sees it: its work area cut into cells of the
/// icon spacing at the monitor's DPI, from the work area's top left corner. Rectangles in physical pixels.
/// </summary>
public sealed record DesktopWorkspace(RectInt32 WorkArea, uint Dpi, bool IsPrimary, int CellWidth, int CellHeight)
{
    public GridSize Grid => new(WorkArea.Width / Math.Max(1, CellWidth), WorkArea.Height / Math.Max(1, CellHeight));

    /// <summary>A cell's size in effective pixels (at 96 DPI).</summary>
    public (double Width, double Height) CellSize => (CellWidth * 96.0 / Dpi, CellHeight * 96.0 / Dpi);

    public string Key => new LayoutWorkspace(0, 0, 0, Grid.Columns, Grid.Rows, IsPrimary ? LayoutWorkspace.PrimaryFlag : 0, []).Key;

    /// <summary>The top left corner of a cell on the screen.</summary>
    public PointInt32 CellOrigin(GridCell cell) => new(WorkArea.X + cell.Column * CellWidth, WorkArea.Y + cell.Row * CellHeight);

    /// <summary>The top left corner on the screen of an icon at a point of the grid (a cell's, while on the grid).</summary>
    public PointInt32 Origin(double x, double y) =>
        new(WorkArea.X + (int)Math.Round(x * CellWidth), WorkArea.Y + (int)Math.Round(y * CellHeight));

    /// <summary>
    /// An icon's position kept within the work area, as Explorer keeps an icon dragged off the grid: the whole cell on
    /// it (measured on the VM: at most 1702,839 for 76x101 cells in 1764x940).
    /// </summary>
    public IconPosition Clamp(IconPosition position) => position with
    {
        X = Math.Clamp(position.X, 0, Math.Max(0, WorkArea.Width / (double)CellWidth - 1)),
        Y = Math.Clamp(position.Y, 0, Math.Max(0, WorkArea.Height / (double)CellHeight - 1)),
    };

    /// <summary>A point on the screen as a point of the grid, in cells.</summary>
    public (double X, double Y) GridPoint(PointInt32 point) =>
        ((point.X - WorkArea.X) / (double)CellWidth, (point.Y - WorkArea.Y) / (double)CellHeight);

    /// <summary>
    /// The cell whose top left corner is nearest to a point on the screen (it may be off the grid); halfway rounds up,
    /// as in Explorer.
    /// </summary>
    public GridCell NearestCell(PointInt32 point) => new(
        (int)Math.Floor((point.X - WorkArea.X) / (double)CellWidth + 0.5),
        (int)Math.Floor((point.Y - WorkArea.Y) / (double)CellHeight + 0.5));

    /// <summary>How far a point on the screen is from the work area: 0 inside it.</summary>
    public long DistanceSquared(PointInt32 point)
    {
        long dx = point.X < WorkArea.X ? WorkArea.X - point.X : Math.Max(0, point.X - (WorkArea.X + WorkArea.Width - 1));
        long dy = point.Y < WorkArea.Y ? WorkArea.Y - point.Y : Math.Max(0, point.Y - (WorkArea.Y + WorkArea.Height - 1));
        return dx * dx + dy * dy;
    }
}

/// <summary>
/// Where the desktop's icons were left, in Explorer's saved layouts (<see cref="IconLayouts"/>), for the monitors there
/// are now; and the layouts with the icons' places now. Explorer keeps a layout per monitor arrangement and finds it
/// again by each monitor's grid size, so icons dragged to a second monitor go back there when it comes back.
/// </summary>
public static class DesktopLayout
{
    /// <summary>The space Explorer gives an icon's label: two lines of the icon font, at 96 DPI.</summary>
    private const int LabelHeight = 44;

    /// <summary>
    /// The icon spacing Explorer's desktop uses (in pixels at 96 DPI), as shell32's <c>CListViewHost::UpdateIconSpacing</c>
    /// works it out: a cell around the icon (at least the system's 75 pixels wide) and its two-line label, then
    /// stretched to share out what's left of the work areas: across, so the columns fill the first work area (or
    /// leave the least over across all of them); down, by what's over beyond 30% of a row. Measured on the VM with
    /// <c>LVM_GETITEMSPACING</c>: 76x103 for medium icons with the two monitors, 76x101 with the primary alone.
    /// </summary>
    /// <param name="workAreas">Each monitor's work area at 96 DPI, in work-area order.</param>
    public static (int Width, int Height) Spacing(int iconSize, IReadOnlyList<(int Width, int Height)> workAreas)
    {
        int box = iconSize + 6 + (int)Math.Round(9 * (iconSize - 16) / 240.0, MidpointRounding.AwayFromZero);
        int width = Math.Max(75, box);
        int height = box + LabelHeight;
        if (workAreas.Count == 0)
            return (width, height);
        if (workAreas.Count == 1)
            return (Stretch(workAreas[0].Width, width), RowSpacing(workAreas[0].Height, height));

        // Columns: the first work area's stretch, unless a width between the plain one and the smallest stretch
        // fills the first nearly as well and leaves less over in all of them.
        int widthBound = MaxBound(workAreas.Select(area => area.Width), width);
        int bestWidth = Stretch(workAreas[0].Width, width);
        int leastOver = workAreas.Sum(area => area.Width % bestWidth);
        for (int candidate = width; candidate < widthBound; candidate++)
        {
            int over = workAreas.Sum(area => area.Width % candidate);
            if (workAreas[0].Width % candidate < (int)(width * 0.1f) && over < leastOver)
                (bestWidth, leastOver) = (candidate, over);
        }

        // Rows: the tallest any work area wants, up to the smallest stretch.
        int heightBound = MaxBound(workAreas.Select(area => area.Height), height);
        int bestHeight = height;
        foreach ((int _, int areaHeight) in workAreas)
        {
            int wanted = RowSpacing(areaHeight, height);
            if (wanted > heightBound)
                return (bestWidth, heightBound);
            bestHeight = Math.Max(bestHeight, wanted);
        }
        return (bestWidth, bestHeight);
    }

    /// <summary>The spacing that shares a length's remainder out among its cells.</summary>
    private static int Stretch(int length, int spacing) => spacing + length % spacing / Math.Max(1, length / spacing);

    /// <summary>Rows share out only what's over beyond 30% of a row.</summary>
    private static int RowSpacing(int length, int spacing)
    {
        int slack = (int)(spacing * 0.3f);
        return length % spacing > slack ? spacing + (length % spacing - slack) / Math.Max(1, length / spacing) : spacing;
    }

    private static int MaxBound(IEnumerable<int> lengths, int spacing) =>
        lengths.Aggregate(spacing * 2, (bound, length) => Math.Min(bound, Stretch(length, spacing)));

    /// <summary>
    /// The monitors' workspaces in Explorer's order (by work area, left to right, then top to bottom), with the icon
    /// spacing (<see cref="Spacing"/>) scaled to each one's DPI as Explorer does it (whole pixels, rounded down).
    /// </summary>
    public static IReadOnlyList<DesktopWorkspace> Workspaces(IEnumerable<(RectInt32 WorkArea, uint Dpi, bool IsPrimary)> monitors, int iconSize)
    {
        List<(RectInt32 WorkArea, uint Dpi, bool IsPrimary)> sorted = [.. monitors.OrderBy(monitor => monitor.WorkArea.X).ThenBy(monitor => monitor.WorkArea.Y)];
        (int width, int height) = Spacing(iconSize, [.. sorted.Select(monitor =>
            ((int)(monitor.WorkArea.Width * 96 / Math.Max(1u, monitor.Dpi)), (int)(monitor.WorkArea.Height * 96 / Math.Max(1u, monitor.Dpi))))]);
        return
        [
            .. sorted.Select(monitor => new DesktopWorkspace(
                monitor.WorkArea, monitor.Dpi, monitor.IsPrimary, (int)(monitor.Dpi * width / 96), (int)(monitor.Dpi * height / 96))),
        ];
    }

    public static string Key(IReadOnlyList<DesktopWorkspace> workspaces) => LayoutDesktop.KeyOf(workspaces.Select(workspace => workspace.Key));

    /// <summary>
    /// Where each saved icon was left, by name and flags, on these monitors. The layout saved for these grids if there
    /// is one, or for a primary monitor of another size; else, as Explorer does for an arrangement it hasn't seen, the
    /// saved layout that fits best: each of its monitors goes to one of these (the primary to the primary, then ones
    /// of the same grid size, then in order), and the one whose icons fit those monitors' grids best wins. Icons of a
    /// monitor with no counterpart aren't placed; they join the new icons.
    /// </summary>
    public static Dictionary<(string Name, LayoutIconFlags Flags), IconPosition> SavedPlaces(IconLayouts layouts, IReadOnlyList<DesktopWorkspace> workspaces)
    {
        var places = new Dictionary<(string, LayoutIconFlags), IconPosition>(LayoutNameComparer.Instance);
        if (workspaces.Count == 0)
            return places;

        // This desktop's own layout (or the one Explorer linked it to); else, as Explorer takes for a desktop it hasn't
        // seen, the latest of those only the primary monitor's grid tells apart (the sidebar narrows it while NeoShell
        // is the shell).
        LayoutDesktop? desktop = layouts.Find(Key(workspaces)) is { } own ? (own.LinkedKey is { } link ? layouts.Find(link) : own) : null;
        desktop ??= layouts.Desktops.LastOrDefault(saved => saved.LinkedKey is null && LooksTheSame(saved.Workspaces, workspaces));
        int[] map; // workspace in the saved desktop -> workspace now, or -1
        if (desktop is not null)
        {
            map = [.. Enumerable.Range(0, desktop.Workspaces.Count)];
        }
        else
        {
            (desktop, map) = BestCompatible(layouts, workspaces);
            if (desktop is null)
                return places;
        }

        for (int i = 0; i < desktop.Workspaces.Count; i++)
        {
            if (map[i] < 0 || map[i] >= workspaces.Count)
                continue;
            foreach (LayoutIcon icon in desktop.Workspaces[i].Icons)
                places.TryAdd((icon.Name, icon.Flags), new IconPosition(map[i], icon.X, icon.Y));
        }
        return places;
    }

    /// <summary>
    /// The layouts with the desktop for these monitors (by its key) holding the icons' places now, and every other
    /// desktop as it was. Not over a desktop that differs only in the primary monitor's grid (Explorer's, while the
    /// sidebar narrows NeoShell's): Explorer finds its own by its key, and without it may lay its icons out afresh. That's
    /// as Explorer keeps them: icons moved on a desktop it took another's places for are saved for its own key, leaving
    /// the other's as they were.
    /// </summary>
    public static IconLayouts WithPlaces(
        IconLayouts layouts, IReadOnlyList<DesktopWorkspace> workspaces, IEnumerable<(string Name, LayoutIconFlags Flags, IconPosition Position)> icons)
    {
        LayoutDesktop? saved = layouts.Find(Key(workspaces));
        ILookup<int, (string Name, LayoutIconFlags Flags, IconPosition Position)> byWorkspace = icons.ToLookup(icon => icon.Position.Workspace);
        var desktop = new LayoutDesktop(
            LayoutDesktop.CurrentVersion,
            LinkedKey: null,
            [
                .. workspaces.Select((workspace, index) =>
                {
                    LayoutWorkspace? old = saved?.LinkedKey is null ? saved?.Workspaces.ElementAtOrDefault(index) : null;
                    return new LayoutWorkspace(
                        LayoutWorkspace.CurrentVersion,
                        old?.ShiftX ?? 0,
                        old?.ShiftY ?? 0,
                        workspace.Grid.Columns,
                        workspace.Grid.Rows,
                        workspace.IsPrimary ? LayoutWorkspace.PrimaryFlag : 0,
                        [.. byWorkspace[index].Select(icon => new LayoutIcon(icon.Name, icon.Flags, (float)icon.Position.X, (float)icon.Position.Y))]);
                }),
            ],
            LinkFlags: null);
        return layouts.With(desktop);
    }

    /// <summary>
    /// Whether Explorer takes a saved desktop for the one of these monitors: as many monitors, the primary in the same
    /// place, and every other one of the same grid size. The primary monitor's grid may differ.
    /// </summary>
    private static bool LooksTheSame(IReadOnlyList<LayoutWorkspace> saved, IReadOnlyList<DesktopWorkspace> workspaces)
    {
        if (saved.Count != workspaces.Count)
            return false;
        for (int i = 0; i < saved.Count; i++)
        {
            if (saved[i].IsPrimary != workspaces[i].IsPrimary)
                return false;
            if (!saved[i].IsPrimary && (saved[i].Columns != workspaces[i].Grid.Columns || saved[i].Rows != workspaces[i].Grid.Rows))
                return false;
        }
        return true;
    }

    private static (LayoutDesktop? Desktop, int[] Map) BestCompatible(IconLayouts layouts, IReadOnlyList<DesktopWorkspace> workspaces)
    {
        (LayoutDesktop? Desktop, int[] Map, int Score, int CountDifference) best = (null, [], 0, int.MaxValue);
        foreach (LayoutDesktop desktop in layouts.Desktops)
        {
            if (desktop.LinkedKey is not null || desktop.Workspaces.All(workspace => workspace.Icons.Count == 0))
                continue;

            int[] map = Map(desktop.Workspaces, workspaces);
            int score = int.MaxValue;
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i] >= 0 && desktop.Workspaces[i].Icons.Count > 0)
                    score = Math.Min(score, Compatibility(desktop.Workspaces[i], workspaces[map[i]].Grid));
            }
            if (score == int.MaxValue)
                continue; // none of its icons would show

            // Later desktops were saved more recently, so they win a tie.
            int countDifference = Math.Abs(desktop.Workspaces.Count - workspaces.Count);
            if (score > best.Score || (score == best.Score && countDifference <= best.CountDifference))
                best = (desktop, map, score, countDifference);
        }
        return (best.Desktop, best.Map);
    }

    /// <summary>
    /// How well a saved monitor's icons fit a grid, as Explorer rates it: 4 the same grid, 3 a grid that holds the
    /// rectangle the icons span, 1 one that doesn't.
    /// </summary>
    private static int Compatibility(LayoutWorkspace saved, GridSize grid)
    {
        if (saved.Columns == grid.Columns && saved.Rows == grid.Rows)
            return 4;
        float width = saved.Icons.Max(icon => icon.X) + 1 - saved.Icons.Min(icon => icon.X);
        float height = saved.Icons.Max(icon => icon.Y) + 1 - saved.Icons.Min(icon => icon.Y);
        return width <= grid.Columns && height <= grid.Rows ? 3 : 1;
    }

    /// <summary>
    /// Which monitor now each saved one becomes: the primary the primary, then each other monitor the first saved one
    /// left of the same grid size, then the first left at all.
    /// </summary>
    private static int[] Map(IReadOnlyList<LayoutWorkspace> saved, IReadOnlyList<DesktopWorkspace> workspaces)
    {
        int[] map = [.. saved.Select(_ => -1)];
        int savedPrimary = saved.Select((workspace, index) => (workspace, index)).FirstOrDefault(pair => pair.workspace.IsPrimary, (null!, -1)).index;
        int primary = workspaces.Select((workspace, index) => (workspace, index)).FirstOrDefault(pair => pair.workspace.IsPrimary, (null!, -1)).index;
        if (savedPrimary >= 0 && primary >= 0)
            map[savedPrimary] = primary;

        var unmapped = Enumerable.Range(0, workspaces.Count).Where(index => index != primary || savedPrimary < 0).ToList();
        foreach (int target in unmapped.ToList())
        {
            int source = Enumerable.Range(0, saved.Count).FirstOrDefault(
                index => map[index] < 0 && saved[index].Columns == workspaces[target].Grid.Columns && saved[index].Rows == workspaces[target].Grid.Rows, -1);
            if (source >= 0)
            {
                map[source] = target;
                unmapped.Remove(target);
            }
        }
        foreach (int target in unmapped)
        {
            int source = Array.IndexOf(map, -1);
            if (source < 0)
                break;
            map[source] = target;
        }
        return map;
    }
}
