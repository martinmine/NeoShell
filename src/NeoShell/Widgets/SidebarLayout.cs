using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Widgets;

/// <summary>Where the widget sidebar and floating widgets go, in physical pixels, and the order widgets are kept in.</summary>
public static class SidebarLayout
{
    /// <summary>The narrowest and widest the sidebar can be dragged, in effective pixels.</summary>
    public const double MinWidth = 240;
    public const double MaxWidth = 560;

    /// <summary>A floating widget's width in effective pixels, and how narrow and wide one that can be resized gets.</summary>
    public const double FloatingWidth = 300;
    public const double FloatingMinWidth = 200;
    public const double FloatingMaxWidth = 640;

    /// <summary>How short and tall the resizable part of a widget (a note's text) gets, in effective pixels.</summary>
    public const double MinContentHeight = 60;
    public const double MaxContentHeight = 900;

    public static int PhysicalWidth(double width, uint dpi) =>
        (int)Math.Round(Math.Clamp(width, MinWidth, MaxWidth) * dpi / 96.0);

    /// <summary>
    /// The strip along the right of <paramref name="workArea"/>: above the taskbar and left of any app bars. It takes
    /// no space of its own; windows maximize over it as over the desktop.
    /// </summary>
    public static RectInt32 Bounds(RectInt32 workArea, int width) =>
        new(workArea.X + workArea.Width - width, workArea.Y, width, workArea.Height);

    /// <summary>
    /// Where a floating widget goes: where it was left, unless that's no longer on any screen (a monitor was
    /// unplugged); then at the top right of <paramref name="fallback"/>.
    /// </summary>
    public static PointInt32 KeepOnScreen(PointInt32 topLeft, int width, IReadOnlyList<RectInt32> workAreas, RectInt32 fallback, int margin)
    {
        // Enough of it to grab must show.
        var grip = new PointInt32(topLeft.X + Math.Min(width, 48) / 2, topLeft.Y + 8);
        return workAreas.Any(area => Contains(area, grip))
            ? topLeft
            : new PointInt32(fallback.X + fallback.Width - width - margin, fallback.Y + margin);
    }

    public static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    /// <summary>Where among the cards a widget dropped at <paramref name="y"/> goes: before the first card whose middle is below it.</summary>
    public static int DropIndex(IReadOnlyList<double> middles, double y) => middles.Count(middle => middle < y);

    /// <summary>
    /// The widgets with <paramref name="id"/> docked in the sidebar as its <paramref name="dockedIndex"/>-th widget
    /// (counting only docked ones). The sidebar's order is the order of the docked widgets in the list.
    /// </summary>
    public static IReadOnlyList<WidgetSettings> Dock(IReadOnlyList<WidgetSettings> widgets, string id, int dockedIndex)
    {
        WidgetSettings widget = widgets.Single(w => w.Id == id) with { X = null, Y = null };
        List<WidgetSettings> others = [.. widgets.Where(w => w.Id != id)];
        int docked = 0;
        int at = others.Count;
        for (int i = 0; i < others.Count; i++)
        {
            if (others[i].IsFloating)
                continue;
            if (docked == dockedIndex)
            {
                at = i;
                break;
            }
            docked++;
        }
        others.Insert(at, widget);
        return others;
    }

    /// <summary>The widgets with <paramref name="widget"/> in place of the one with its id.</summary>
    public static IReadOnlyList<WidgetSettings> Replace(IReadOnlyList<WidgetSettings> widgets, WidgetSettings widget) =>
        [.. widgets.Select(w => w.Id == widget.Id ? widget : w)];
}
