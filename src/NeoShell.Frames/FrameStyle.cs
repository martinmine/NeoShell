namespace NeoShell.Frames;

public enum FrameBackdrop { Default, None, Mica, MicaAlt, Acrylic }

public enum FrameTheme { Default, Light, Dark }

public enum FrameCorners { Default, Square, Round, SmallRound }

/// <summary>A frame drawn over the window by this program's overlay windows, rather than set through DWM.</summary>
public enum FrameOverlay { None, Luna }

/// <summary>
/// A look for other apps' title bars and frames, kept in NeoShell's settings (<c>WindowFrameStyles</c>). Every value
/// is optional: unset (Default, null) leaves the window's own. Colours are <c>#RRGGBB</c>, or
/// <see cref="FrameColors.Accent"/>; a border may also be <see cref="FrameColors.None"/>, and text
/// <see cref="FrameColors.Contrast"/> (white or black, whichever reads on the caption colour).
/// </summary>
public sealed record FrameStyle
{
    public string Name { get; set; } = "";
    public FrameBackdrop Backdrop { get; set; }
    public FrameTheme Theme { get; set; }
    public string? CaptionColor { get; set; }
    public string? TextColor { get; set; }
    public string? BorderColor { get; set; }
    public FrameCorners Corners { get; set; }
    /// <summary>DWM stops drawing the frame and the app draws uxtheme's basic one (Windows 7 Basic's look).</summary>
    public bool BasicFrame { get; set; }
    /// <summary>
    /// Drawn by NeoShell.Frames.exe over the window instead; the values above are then left to it. Only while that
    /// program runs: without it the window keeps Windows' frame.
    /// </summary>
    public FrameOverlay Overlay { get; set; }
}

/// <summary>
/// The style for an app's windows, by process name (<c>notepad.exe</c>) and optionally window class; a null
/// <paramref name="Style"/> leaves them alone.
/// </summary>
public sealed record FrameRule(string ProcessName, string? ClassName = null, string? Style = null);
