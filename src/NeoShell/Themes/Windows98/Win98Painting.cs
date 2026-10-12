using static NeoShell.Themes.Windows98.Win98Art;

namespace NeoShell.Themes.Windows98;

/// <summary>
/// 98's 3D edges, as measured: two-pixel bevels for buttons, menus and windows, one-pixel ones for flat toolbar buttons
/// and the notification area, etched lines and grippers between the taskbar's bands. The bottom and right edges are
/// drawn last, so they take the corners they share with the top and left.
/// </summary>
public static class Win98Painting
{
    /// <summary>A raised button (the Start button, a task button): white and light grey outside in at the top left, black and dark grey at the bottom right.</summary>
    public static void RaisedButton(ThemeCanvas canvas, int x, int y, int width, int height) =>
        Edge(canvas, x, y, width, height, Highlight, Light, Shadow, DarkShadow);

    /// <summary>A pushed button: the raised one's colours swapped.</summary>
    public static void PushedButton(ThemeCanvas canvas, int x, int y, int width, int height) =>
        Edge(canvas, x, y, width, height, DarkShadow, Shadow, Light, Highlight);

    /// <summary>A menu's or Start's frame: light grey then white at the top left.</summary>
    public static void RaisedFrame(ThemeCanvas canvas, int x, int y, int width, int height) =>
        Edge(canvas, x, y, width, height, Light, Highlight, Shadow, DarkShadow);

    /// <summary>A one-pixel raised edge: a flat toolbar button under the pointer, a band's gripper.</summary>
    public static void ThinRaised(ThemeCanvas canvas, int x, int y, int width, int height) =>
        Line(canvas, x, y, width, height, Highlight, Shadow);

    /// <summary>A one-pixel sunken edge: a pressed toolbar button, the notification area.</summary>
    public static void ThinSunken(ThemeCanvas canvas, int x, int y, int width, int height) =>
        Line(canvas, x, y, width, height, Shadow, Highlight);

    /// <summary>The etched line at a band's left: dark grey, then white.</summary>
    public static void EtchedLine(ThemeCanvas canvas, int x, int y, int height)
    {
        canvas.Fill(x, y, 1, height, Shadow);
        canvas.Fill(x + 1, y, 1, height, Highlight);
    }

    /// <summary>A band's gripper: a raised bar 3 wide.</summary>
    public static void Gripper(ThemeCanvas canvas, int x, int y, int height) => ThinRaised(canvas, x, y, 3, height);

    /// <summary>
    /// The pushed task button's face: a white row, then white and the face colour alternating like a chessboard (white
    /// where <c>x + y</c> is odd, counted from <paramref name="originX"/>, <paramref name="originY"/>).
    /// </summary>
    public static void Checker(ThemeCanvas canvas, int x, int y, int width, int height, int originX, int originY)
    {
        canvas.Fill(x, y, width, 1, Highlight);
        for (int row = y + 1; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
                canvas.Set(column, row, ((column - originX + row - originY) & 1) == 1 ? Highlight : Face);
        }
    }

    /// <summary>A dotted focus rectangle: black where the offset from its top left corner, across plus down, is odd.</summary>
    public static void FocusRectangle(ThemeCanvas canvas, int x, int y, int width, int height)
    {
        void Dot(int column, int row)
        {
            if (((column - x + row - y) & 1) == 1)
                canvas.Set(column, row, Text);
        }
        for (int column = x; column < x + width; column++)
        {
            Dot(column, y);
            Dot(column, y + height - 1);
        }
        for (int row = y; row < y + height; row++)
        {
            Dot(x, row);
            Dot(x + width - 1, row);
        }
    }

    private static void Edge(ThemeCanvas canvas, int x, int y, int width, int height, uint outerTopLeft, uint innerTopLeft, uint innerBottomRight, uint outerBottomRight)
    {
        Line(canvas, x, y, width, height, outerTopLeft, outerBottomRight);
        Line(canvas, x + 1, y + 1, width - 2, height - 2, innerTopLeft, innerBottomRight);
    }

    private static void Line(ThemeCanvas canvas, int x, int y, int width, int height, uint topLeft, uint bottomRight)
    {
        canvas.Fill(x, y, width - 1, 1, topLeft);
        canvas.Fill(x, y, 1, height - 1, topLeft);
        canvas.Fill(x, y + height - 1, width, 1, bottomRight);
        canvas.Fill(x + width - 1, y, 1, height, bottomRight);
    }
}
