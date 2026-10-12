namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// A horizontal piece of XP's art that stretches: measured columns at its left and right ends, and one colour per row
/// in between (XP stretched a small image; the middle's columns differ by a level or two, which this leaves out).
/// </summary>
public sealed class XpBand
{
    private readonly uint[][] _left;
    private readonly uint[] _middle;
    private readonly uint[][] _right;

    public XpBand(string[] left, string middle, string[] right)
    {
        _left = [.. left.Select(XpArt.Colors)];
        _middle = XpArt.Colors(middle);
        _right = [.. right.Select(XpArt.Colors)];
    }

    public int Rows => _middle.Length;

    /// <summary>The colour (RRGGBB) at column <paramref name="x"/> of <paramref name="row"/>, in a piece <paramref name="width"/> wide.</summary>
    public uint Pixel(int x, int row, int width)
    {
        int fromRight = width - 1 - x;
        int right = _right[row].Length;
        if (fromRight < right)
            return _right[row][right - 1 - fromRight];
        return x < _left[row].Length ? _left[row][x] : _middle[row];
    }

    /// <summary>Draws the piece <paramref name="width"/> wide with its top left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Draw(ThemeCanvas canvas, int x, int y, int width)
    {
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < width; column++)
                canvas.Set(x + column, y + row, Pixel(column, row, width));
        }
    }

    /// <summary>The middle's colour on a row, for what's drawn over it.</summary>
    public uint Middle(int row) => _middle[row];
}
