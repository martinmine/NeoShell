namespace NeoShell.Themes.WindowsXp;

/// <summary>Pieces of XP's look shared by its menus, tooltips and Start, drawn from what was measured on XP.</summary>
public static class XpPainting
{
    // The shadow's opacity fading in along a side, a few pixels after the corner (Start's right side reaches full
    // strength some ten rows below its top).
    private const int ShadowStart = 5;

    /// <summary>
    /// XP's drop shadow to the right of and below a rectangle (the rectangle is drawn over it afterwards): black,
    /// <see cref="XpArt.DropShadow"/> from each edge outwards, fading in from the corners.
    /// </summary>
    public static void DropShadow(ThemeCanvas canvas, int x, int y, int width, int height)
    {
        double[] profile = XpArt.DropShadow;
        int reach = profile.Length;
        for (int row = y + ShadowStart; row < y + height + reach; row++)
        {
            double down = Along(row - y, height, profile);
            for (int column = x + ShadowStart; column < x + width + reach; column++)
            {
                if (column < x + width && row < y + height)
                    continue;
                canvas.Blend(column, row, 0x000000, down * Along(column - x, width, profile));
            }
        }
    }

    // How strong the shadow is at an offset along one axis: fading in after the start, full along the side, then the
    // profile beyond the far edge.
    private static double Along(int offset, int length, double[] profile)
    {
        if (offset >= length)
            return offset - length < profile.Length ? profile[offset - length] : 0;
        int fromStart = offset - ShadowStart;
        return fromStart < profile.Length ? profile[profile.Length - 1 - fromStart] / profile[0] : 1;
    }

    /// <summary>
    /// The separator of Start and its menus: a line that fades in over 70 pixels from the background to its colour
    /// (<c>D3D3C8</c> on white, <c>81B6FF</c> in Start's blue column) and out again, with a lighter line under it.
    /// </summary>
    public static void FadingLine(ThemeCanvas canvas, int x0, int x1, int y, uint background = 0xFEFEFE, uint line = 0xD3D3C8, uint under = 0xFEFEFE)
    {
        const double Ramp = 70;
        for (int x = x0; x < x1; x++)
        {
            double edge = Math.Min(1, Math.Min(x - x0, x1 - 1 - x) / Ramp);
            double strength = 1 - (1 - edge) * (1 - edge);
            canvas.Set(x, y, Mix(background, line, strength));
            canvas.Set(x, y + 1, Mix(background, under, strength));
        }
    }

    public static uint Mix(uint a, uint b, double t)
    {
        uint Channel(int shift) => (uint)Math.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
