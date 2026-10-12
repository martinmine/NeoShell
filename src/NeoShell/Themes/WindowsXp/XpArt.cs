using NeoShell.Interop.Imaging;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Windows XP's default blue Luna taskbar and Start menu (96 DPI) as numbers measured from XP's own screen; see
/// docs/design/theme-xp.md. The tables (XpArt.*.cs) were written out by the measuring scripts; colours are RRGGBB
/// (AARRGGBB where they have an opacity), rows top to bottom.
/// </summary>
public static partial class XpArt
{
    /// <summary>Taskbar, menus and tooltips: Tahoma 8 pt.</summary>
    public static readonly GdiFont Tahoma = new("Tahoma", 11, Bold: false);

    public static readonly GdiFont TahomaBold = new("Tahoma", 11, Bold: true);

    /// <summary>The Start button's "start": Franklin Gothic Medium italic, 19 px.</summary>
    public static readonly GdiFont StartFont = new("Franklin Gothic Medium", 19, Bold: false, Italic: true);

    /// <summary>The user's name at the top of Start: Franklin Gothic Medium, 19 px.</summary>
    public static readonly GdiFont UserNameFont = new("Franklin Gothic Medium", 19, Bold: false);

    public const uint White = 0xFFFFFF;

    /// <summary>A group button's count of windows ("10 Notepad"), bold.</summary>
    public const uint GroupCount = 0xFFCC6D;

    /// <summary>Tooltips: black text and border on XP's info colour.</summary>
    public const uint ToolTipBackground = 0xFFFFE1;

    /// <summary>The soft drop shadow of XP's menus and tooltips, from the edge outwards (opacity of black).</summary>
    public static readonly double[] DropShadow = [0.44, 0.33, 0.17, 0.055, 0.012];

    /// <summary>A flashing task button. Not measured (XP's flashing couldn't be caught); the hot button for now.</summary>
    public static XpBand TaskButtonFlashing => TaskButtonHot;

    public static uint[] Colors(string colors) => [.. colors.Split(' ').Select(hex => Convert.ToUInt32(hex, 16))];

    private static uint[][] Rows(string[] rows) => [.. rows.Select(Colors)];
}
