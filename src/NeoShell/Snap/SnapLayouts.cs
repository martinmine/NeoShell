using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>A zone of a snap layout, as fractions of the work area.</summary>
public sealed record SnapZone(double X, double Y, double Width, double Height);

/// <summary>The layouts Win+Z offers, as Windows 11's Snap layouts: more of them on a wide screen.</summary>
public static class SnapLayouts
{
    private const double Third = 1.0 / 3;

    /// <summary>The layouts for a work area, in pixels at <paramref name="dpi"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<SnapZone>> For(RectInt32 workArea, uint dpi)
    {
        if (workArea.Height > workArea.Width)
        {
            // Portrait: stacked, top to bottom.
            return
            [
                [new(0, 0, 1, 0.5), new(0, 0.5, 1, 0.5)],
                [new(0, 0, 1, Third), new(0, Third, 1, Third), new(0, 2 * Third, 1, Third)],
            ];
        }

        IReadOnlyList<SnapZone> halves = [new(0, 0, 0.5, 1), new(0.5, 0, 0.5, 1)];
        IReadOnlyList<SnapZone> twoThirds = [new(0, 0, 2 * Third, 1), new(2 * Third, 0, Third, 1)];
        IReadOnlyList<SnapZone> halfAndStack = [new(0, 0, 0.5, 1), new(0.5, 0, 0.5, 0.5), new(0.5, 0.5, 0.5, 0.5)];
        IReadOnlyList<SnapZone> quarters = [new(0, 0, 0.5, 0.5), new(0.5, 0, 0.5, 0.5), new(0, 0.5, 0.5, 0.5), new(0.5, 0.5, 0.5, 0.5)];
        // Columns of a third and the wide middle only once there's room for them: 1920 effective pixels, as Windows.
        if (workArea.Width * 96.0 / dpi < 1920)
            return [halves, twoThirds, halfAndStack, quarters];

        IReadOnlyList<SnapZone> thirds = [new(0, 0, Third, 1), new(Third, 0, Third, 1), new(2 * Third, 0, Third, 1)];
        IReadOnlyList<SnapZone> wideMiddle = [new(0, 0, 0.25, 1), new(0.25, 0, 0.5, 1), new(0.75, 0, 0.25, 1)];
        return [halves, twoThirds, thirds, halfAndStack, quarters, wideMiddle];
    }

    /// <summary>
    /// The zone's place in the work area, in pixels. Neighbouring zones share their edge exactly: each edge is rounded
    /// on its own, not the width.
    /// </summary>
    public static RectInt32 Bounds(SnapZone zone, RectInt32 workArea)
    {
        int left = workArea.X + (int)Math.Round(zone.X * workArea.Width);
        int top = workArea.Y + (int)Math.Round(zone.Y * workArea.Height);
        int right = workArea.X + (int)Math.Round((zone.X + zone.Width) * workArea.Width);
        int bottom = workArea.Y + (int)Math.Round((zone.Y + zone.Height) * workArea.Height);
        return new RectInt32(left, top, right - left, bottom - top);
    }
}
