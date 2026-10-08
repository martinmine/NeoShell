using NeoShell.Interop.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Capture;

/// <summary>
/// A freeform snip's shape: the pointer's path, smoothed as Snipping Tool's ink smooths it and closed by a straight
/// line back to the start, and what it cuts out of the picture: the path's bounding box, transparent outside the
/// path (even-odd, as Snipping Tool fills a path that crosses itself), with antialiased edges.
/// </summary>
public static class FreeformPath
{
    // Rows of samples per pixel; across a row, coverage is exact.
    private const int Samples = 4;

    /// <summary>
    /// Smooths the points with quadratic curves through the midpoints between them, each point the control of its
    /// curve, flattened to lines no longer than about two pixels. The first and last points stay where they are.
    /// </summary>
    public static IReadOnlyList<Point> Smooth(IReadOnlyList<Point> points)
    {
        if (points.Count < 3)
            return points;

        var smoothed = new List<Point> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            Point start = Midpoint(points[i - 1], points[i]);
            Point control = points[i];
            Point end = Midpoint(points[i], points[i + 1]);
            if (i == 1)
                smoothed.Add(start);
            double length = Distance(start, control) + Distance(control, end);
            int steps = Math.Clamp((int)Math.Ceiling(length / 2), 1, 16);
            for (int step = 1; step <= steps; step++)
            {
                double t = (double)step / steps, u = 1 - t;
                smoothed.Add(new Point(
                    u * u * start.X + 2 * u * t * control.X + t * t * end.X,
                    u * u * start.Y + 2 * u * t * control.Y + t * t * end.Y));
            }
        }
        smoothed.Add(points[^1]);
        return smoothed;
    }

    /// <summary>The whole pixels the closed path touches, within <paramref name="clip"/> (empty for none).</summary>
    public static RectInt32 Bounds(IReadOnlyList<Point> path, RectInt32 clip)
    {
        if (path.Count == 0)
            return default;
        int left = Math.Max(clip.X, (int)Math.Floor(path.Min(p => p.X)));
        int top = Math.Max(clip.Y, (int)Math.Floor(path.Min(p => p.Y)));
        int right = Math.Min(clip.X + clip.Width, (int)Math.Ceiling(path.Max(p => p.X)));
        int bottom = Math.Min(clip.Y + clip.Height, (int)Math.Ceiling(path.Max(p => p.Y)));
        return right > left && bottom > top ? new RectInt32(left, top, right - left, bottom - top) : default;
    }

    /// <summary>How much of each pixel of <paramref name="bounds"/> the closed path covers, 0 to 255, row by row.</summary>
    public static byte[] Mask(IReadOnlyList<Point> path, RectInt32 bounds)
    {
        var mask = new byte[bounds.Width * bounds.Height];
        var coverage = new double[bounds.Width];
        var crossings = new List<double>();
        for (int row = 0; row < bounds.Height; row++)
        {
            Array.Clear(coverage);
            for (int sample = 0; sample < Samples; sample++)
            {
                double y = bounds.Y + row + (sample + 0.5) / Samples;
                crossings.Clear();
                for (int i = 0; i < path.Count; i++)
                {
                    Point a = path[i], b = path[(i + 1) % path.Count];
                    if (a.Y <= y != b.Y <= y)
                        crossings.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y) - bounds.X);
                }
                crossings.Sort();
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                    Cover(coverage, crossings[i], crossings[i + 1]);
            }
            for (int x = 0; x < bounds.Width; x++)
                mask[row * bounds.Width + x] = (byte)Math.Round(Math.Min(1, coverage[x] / Samples) * 255);
        }
        return mask;
    }

    /// <summary>
    /// The part of the opaque <paramref name="picture"/> at <paramref name="bounds"/> (picture pixels) with
    /// <paramref name="mask"/> as its alpha (premultiplied, as <see cref="IconBitmap"/>s are).
    /// </summary>
    public static IconBitmap Cut(IconBitmap picture, RectInt32 bounds, byte[] mask)
    {
        var pixels = new byte[bounds.Width * bounds.Height * 4];
        for (int row = 0; row < bounds.Height; row++)
        {
            Array.Copy(picture.Pixels, ((bounds.Y + row) * picture.Width + bounds.X) * 4, pixels, row * bounds.Width * 4, bounds.Width * 4);
            for (int x = 0; x < bounds.Width; x++)
            {
                int i = row * bounds.Width + x;
                for (int c = 0; c < 3; c++)
                    pixels[i * 4 + c] = (byte)((pixels[i * 4 + c] * mask[i] + 127) / 255);
                pixels[i * 4 + 3] = mask[i];
            }
        }
        return new IconBitmap(bounds.Width, bounds.Height, pixels);
    }

    /// <summary>
    /// The picture drawn on white, opaque: a freeform snip as a bitmap on the clipboard, where Snipping Tool's is
    /// white outside the path.
    /// </summary>
    public static IconBitmap OnWhite(IconBitmap picture)
    {
        var pixels = (byte[])picture.Pixels.Clone();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Premultiplied: what shows through is white times what's missing.
            int missing = 255 - pixels[i + 3];
            for (int c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Min(255, pixels[i + c] + missing);
            pixels[i + 3] = 255;
        }
        return new IconBitmap(picture.Width, picture.Height, pixels);
    }

    // Adds a run of one sample row from left to right (in pixels of the row) to each pixel's coverage.
    private static void Cover(double[] coverage, double left, double right)
    {
        left = Math.Max(0, left);
        right = Math.Min(coverage.Length, right);
        for (int x = (int)left; x < right; x++)
            coverage[x] += Math.Min(right, x + 1) - Math.Max(left, x);
    }

    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
