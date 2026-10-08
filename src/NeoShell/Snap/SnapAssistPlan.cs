using NeoShell.Switcher;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>What Snap Assist offers once a window has snapped, and how it lays its cards out, as Explorer's.</summary>
public static class SnapAssistPlan
{
    // Effective pixels, measured on Explorer's: a card is a 40 high title over the preview, 24 apart; the cards keep
    // 18 from the panel's sides.
    public const double HeaderHeight = 40;
    public const double Spacing = 24;
    public const double SidePadding = 18;
    public const double TopPadding = 12;

    /// <summary>
    /// The layouts a snapped window can be part of: Snap layouts' own, and a half with two quarters the other way
    /// round (a quarter snapped beside a window in the right half leaves the other quarter).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<RectInt32>> Layouts(RectInt32 workArea, uint dpi)
    {
        var layouts = SnapLayouts.For(workArea, dpi)
            .Select(layout => (IReadOnlyList<RectInt32>)[.. layout.Select(zone => SnapLayouts.Bounds(zone, workArea))])
            .ToList();
        if (workArea.Width >= workArea.Height)
        {
            layouts.Add([
                SnapLayouts.Bounds(new SnapZone(0, 0, 0.5, 0.5), workArea),
                SnapLayouts.Bounds(new SnapZone(0, 0.5, 0.5, 0.5), workArea),
                SnapLayouts.Bounds(new SnapZone(0.5, 0, 0.5, 1), workArea),
            ]);
        }
        return layouts;
    }

    /// <summary>
    /// The layout a window just snapped to <paramref name="snapped"/> completes, and its zones still empty, in the
    /// layout's order: of the layouts with that zone, the one the other snapped windows (<paramref name="filled"/>,
    /// where each is drawn) fill most of; of those, one of zones all the zone's size (halves for a half, quarters for
    /// a quarter), then the one with fewest zones. Null when no layout has the zone.
    /// </summary>
    /// <remarks>
    /// As in Explorer: a half alone leaves the other half; a quarter alone, the other three quarters; a quarter
    /// beside a window snapped to the other half, the last quarter.
    /// </remarks>
    public static (IReadOnlyList<RectInt32> Layout, IReadOnlyList<RectInt32> Empty)? EmptyZones(
        IReadOnlyList<IReadOnlyList<RectInt32>> layouts, RectInt32 snapped, IReadOnlyCollection<RectInt32> filled)
    {
        IReadOnlyList<RectInt32>? best = layouts
            .Where(layout => layout.Contains(snapped))
            .OrderByDescending(layout => layout.Count(zone => zone != snapped && filled.Contains(zone)))
            .ThenByDescending(layout => layout.All(zone => Math.Abs(zone.Width - snapped.Width) <= 1 && Math.Abs(zone.Height - snapped.Height) <= 1))
            .ThenBy(layout => layout.Count)
            .FirstOrDefault();
        if (best is null)
            return null;
        return (best, [.. best.Where(zone => zone != snapped && !filled.Contains(zone))]);
    }

    /// <summary>
    /// The cards' previews for windows of <paramref name="aspects"/> (width over height) in a panel of this size
    /// (effective pixels): in rows as Alt+Tab's, as many rows as the square root of their number (rounded), the
    /// previews as high as fits that across and down. Returns the preview height, the slots and the grid's width.
    /// </summary>
    public static (double PreviewHeight, IReadOnlyList<SwitcherSlot> Slots, double Width) Arrange(
        IReadOnlyList<double> aspects, double panelWidth, double panelHeight)
    {
        int rows = Math.Max(1, (int)Math.Round(Math.Sqrt(aspects.Count)));
        double width = panelWidth - 2 * SidePadding;
        double height = panelHeight - 2 * TopPadding;
        for (double preview = Math.Floor(height - HeaderHeight); ; preview--)
        {
            (IReadOnlyList<SwitcherSlot> slots, double gridWidth) = AltTabLayout.Arrange([.. aspects.Select(a => a * preview)], width, Spacing);
            int used = slots.Count == 0 ? 0 : slots.Max(s => s.Row) + 1;
            if ((used <= rows && used * (preview + HeaderHeight) + (used - 1) * Spacing <= height && gridWidth <= width) || preview <= 24)
                return (preview, slots, gridWidth);
        }
    }
}
