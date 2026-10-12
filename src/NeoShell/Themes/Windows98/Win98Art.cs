using NeoShell.Interop.Imaging;

namespace NeoShell.Themes.Windows98;

/// <summary>
/// Windows 98's look in its default "Windows Standard" scheme at 96 DPI, as measured from 98's own screen; see
/// docs/design/theme-98.md. Colours are RRGGBB.
/// </summary>
public static class Win98Art
{
    /// <summary>Everything on the taskbar and in Start: MS Sans Serif 8 pt, the bitmap font 98 drew it in.</summary>
    public static readonly GdiFont Font = new("MS Sans Serif", 11, Bold: false);

    /// <summary>"Start", and the active window's task button.</summary>
    public static readonly GdiFont BoldFont = new("MS Sans Serif", 11, Bold: true);

    // The scheme's 3D colours.
    public const uint Face = 0xC0C0C0;
    public const uint Light = 0xDFDFDF;
    public const uint Highlight = 0xFFFFFF;
    public const uint Shadow = 0x808080;
    public const uint DarkShadow = 0x000000;
    public const uint Text = 0x000000;

    /// <summary>A menu's selected item: white text on navy.</summary>
    public const uint Selection = 0x000080;

    public const uint SelectionText = 0xFFFFFF;

    /// <summary>Start's banner above its art: navy.</summary>
    public const uint Banner = 0x000080;

    private static readonly Dictionary<char, uint> s_palette = new()
    {
        ['K'] = 0x000000, ['R'] = 0xFF0000, ['G'] = 0x00FF00, ['B'] = 0x0000FF, ['Y'] = 0xFFFF00,
        ['n'] = 0x000080, ['o'] = 0xFFFFFF, ['+'] = 0x808080, ['.'] = 0xC0C0C0,
    };

    /// <summary>The Start button's Windows flag, 16x16 (a space is the button's face showing through).</summary>
    public static readonly string[] Flag =
    [
        "                ",
        "         KKKK   ",
        "       KKKKKKKK ",
        "K     KKKRKKGKKK",
        "K K KKKKRRKKGGKK",
        "  KKKKKKRRKKGGKK",
        "R     KKRKKKKGKK",
        "R R RRKKKKKKKKKK",
        "  RRRRKKKBKKYKKK",
        "B     KKBBKKYYKK",
        "B B BBKKBBKKYYKK",
        "  BBBBKKBKKKKYKK",
        "K     KKKKKKKKKK",
        "K K KKKKK    KKK",
        "  KKKKK        K",
        "                ",
    ];

    /// <summary>
    /// The bottom of Start's banner, 21x105: "Windows98" reading upwards in white over navy dithered with blue, as 98's
    /// bitmap has it. Above it the banner is plain navy.
    /// </summary>
    public static readonly string[] BannerArt =
    [
        "nnnnnnnnnnnn+..nnnnnn", "nnnnnooo.nnooooonnnnn", "nnnno.nno+.o+nno.nnnn", "nnn+onnnnoo+nnn.onnnn",
        "nnn+onnnnoonnnnnonnnn", "nnn+onnnnoonnnn+onnnn", "nnn+onnnnoonnnn+onnnn", "nnnnoonnooo+nnn.onnnn",
        "nnnn+oooo+no+B.o+nnnn", "nnnnn+..Bnn.ooo.Bnnnn", "nnnnnnnnnnnnB++nnnnnn", "nnnnnnB+.....nBnnnnnn",
        "nnnnn.oooooooo.nnnnnn", "nnnn+o.++o+n+.oonnnnn", "nnnBo.nBnno+BnnoonBnn", "nBn+onnnnB+onnB+onnnn",
        "nnn+onnnnnBonnnBoBnnn", "nnB+oBnBnn+o+Bn+onnBn", "BnnBo.nnnnoonnn.oBnnn", "nnnn.oo++oo+nB.o+nnBn",
        "nBnnn+oooo+Bnn.+Bnnnn", "nnBnBnnB+BnnBnBnnBnBn", "BnnnnBnnnnnB.oo+nnnnn", "nnBnBnnBo.noooooBBnBn",
        "nBnnnnB.o.BoooooonnnB", "nnBnBnnoo.Booo+ooBnBn", "BnnBnnBooBBoo.BoonBnn", "nnBnnB+oo+.oo++ooBnnB",
        "BnBnBnBooooooB.oonBnn", "nBnBnBn.ooooo+ooonBBn", "BnBnBnnBoooo+Boo+BnnB", "nBnnBnB.+++nBB.+BBnBn",
        "BnBBnBnooo.+BnBBnnBnB", "nBnnBnBooooooo+BBnBnB", "BnBBnBnB+.oooooooBnBn", "nBnBnBnBBB.oooooonBnB",
        "BnBnBnB.oooooooooBnBn", "BnBBnBnooooooo.BnBBnB", "nBBnBBBooooo+BBnBBnBB", "BBnBBnB+.oooooo.+nBBn",
        "BnBnBBnBBB+ooooooBnBB", "BBBBnBB++ooooooooBBnB", "nBnBBnBooooooooo.BnBB", "BnBBnBBoooooo.BBBBBBn",
        "BBBnBBnoo.+BBBBnBnBnB", "BnBBBBB+B+.oo.+BBBBBB", "BBBnBnBB.oooooo.BnBnB", "BnBBBBB+oooooooo+BBBB",
        "BBBnBBBooooooooooBBnB", "BBBBBnBoo.B+B+.ooBBBB", "BnBBBBBooBBBBBBooBBBB", "BBBnBBBooo+B+BoooBBBB",
        "BBBBBBBooooooooooBBBn", "BBBBBBB+oooooooo+BBBB", "BBBBBBBB.oooooo.BBBBB", "BBBBBBBBBB.oooBBBBBBB",
        "BBBB+++++.++++.++BBBB", "BBBBoooooooooooooBBBB", "BBBBoooooooooooooBBBB", "BBBBoooooooooooooBBBB",
        "BBBB+++.o.++++oo+BBBB", "BBBBBBBooBBBBBBooBBBB", "BBBBBBBooo+BB+oooBBBB", "BBBBBBBooooooooooBBBB",
        "BBBBBBBooooooooooBBBB", "BBBBBBBBooooooooBBBBB", "BBBBBBBBBB....+BBBBBB", "BBBBBBBB+o.......BBBB",
        "BBBBBBB.oooooooooBBBB", "BBBBBBBooooooooooBBBB", "BBBBBBBooooooooooBBBB", "BBBBBBBoooBBBBBBBBBBB",
        "BBBBBBB.o.BBBBBBBBBBB", "BBBBBBB.oooooooooBBBB", "BBBBBBBooooooooooBBBB", "BBnBBBBooooooooooBBBB",
        "BBBBBBBooooooooooBBBB", "BBBBnBBBBBBBBBBBBBBBB", "BnB.ooBooooooooooBBBB", "BBB.ooBooooooooooBBBB",
        "BnB.ooBooooooooooBBBB", "BBB.ooBooooooooooBBBB", "BnBB++BBBBBBBBBBBBnBB", "nBB+ooo..BBnBnBBnBBBB",
        "BBnBooooooo.+BBBBBBnB", "nBBB..ooooooooo.+BBBB", "BnBnBBBB..oooooooBnBB", "nBnBBnBnBB.ooooooBBBB",
        "BBBnB++ooooooooooBnBn", "nnBBoooooooooo.+BBBBB", "BnB+oooooo..BBBBBnBnB", "nBnBooooooo++BnBnBnBB",
        "BnBn..oooooooo.+BBBnB", "nBnBBBnB.oooooooonBBn", "BnBnnnBnB+.oooooonBnB", "nBnBB+.ooooooooooBBnB",
        "nnB+ooooooooooo..nnBn", "BnnBooooooo..BBBBBnBB", "nnB+oooo++nBBnnnnnBnn", "nBnB++BnBBBnnnBnBBBnB",
        "nnnBnBnnnnnBBnBnBnnBn", "BnnnBnBnBnBnnBnBnBnnB", "nnBnnnnnnBnnBnnnBnnBn", "nBnnBnBnBnnBnBnBnBnBn",
        "nnnBnnnnnnBnnnnnnnBnn",
    ];

    /// <summary>Draws rows of <see cref="Flag"/>-style art; spaces leave what's below.</summary>
    public static void Draw(ThemeCanvas canvas, string[] art, int x, int y)
    {
        for (int row = 0; row < art.Length; row++)
        {
            for (int column = 0; column < art[row].Length; column++)
            {
                if (s_palette.TryGetValue(art[row][column], out uint rgb))
                    canvas.Set(x + column, y + row, rgb);
            }
        }
    }
}
