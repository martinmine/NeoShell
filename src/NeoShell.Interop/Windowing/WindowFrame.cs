using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>DWM's system backdrops (<c>DWM_SYSTEMBACKDROP_TYPE</c>).</summary>
public enum SystemBackdrop { Auto = 0, None = 1, Mica = 2, Acrylic = 3, MicaAlt = 4 }

/// <summary>DWM's corner preferences (<c>DWM_WINDOW_CORNER_PREFERENCE</c>).</summary>
public enum CornerPreference { Default = 0, Square = 1, Round = 2, SmallRound = 3 }

/// <summary>The parts of a frame <see cref="WindowFrame"/> can set, and put back.</summary>
[Flags]
public enum FrameParts
{
    None = 0,
    Backdrop = 1,
    DarkMode = 2,
    CaptionColor = 4,
    TextColor = 8,
    BorderColor = 16,
    Corners = 32,
    BasicFrame = 64,
}

/// <summary>What to set on a window's frame; null leaves the window's own (Windows' or the app's).</summary>
public sealed record FrameAttributes
{
    public SystemBackdrop? Backdrop { get; init; }
    public bool? DarkMode { get; init; }
    /// <summary>0xAARRGGBB; opaque, it hides the backdrop.</summary>
    public uint? CaptionColor { get; init; }
    public uint? TextColor { get; init; }
    /// <summary>0xAARRGGBB; transparent (alpha 0) takes the border away.</summary>
    public uint? BorderColor { get; init; }
    public CornerPreference? Corners { get; init; }
    /// <summary>DWM stops drawing the frame, so the app's <c>DefWindowProc</c> draws uxtheme's basic one.</summary>
    public bool BasicFrame { get; init; }

    public FrameParts Parts =>
        (Backdrop is null ? 0 : FrameParts.Backdrop)
        | (DarkMode is null ? 0 : FrameParts.DarkMode)
        | (CaptionColor is null ? 0 : FrameParts.CaptionColor)
        | (TextColor is null ? 0 : FrameParts.TextColor)
        | (BorderColor is null ? 0 : FrameParts.BorderColor)
        | (Corners is null ? 0 : FrameParts.Corners)
        | (BasicFrame ? FrameParts.BasicFrame : 0);
}

/// <summary>
/// Restyles another app's title bar and frame through the DWM attributes its own app would set: DWM takes them from
/// any process of the same user, with no code inside the app. An app may set them again itself (dark mode on a theme
/// change), with no notification.
/// </summary>
public static unsafe class WindowFrame
{
    /// <summary>The facts about a top-level window that decide whether and how its frame is styled.</summary>
    /// <param name="ProcessName">The executable's file name, e.g. <c>notepad.exe</c>; null when it can't be read.</param>
    public sealed record Target(int ProcessId, string? ProcessName, string ClassName, string Title, uint Style, uint ExStyle,
        bool IsVisible, bool IsCloaked);

    public static Target Read(nint hwnd)
    {
        int processId = TopLevelWindows.GetProcessId(hwnd);
        char* buffer = stackalloc char[256];
        string className = new(buffer, 0, User32.GetClassName(hwnd, buffer, 256));
        return new Target(
            processId,
            Path.GetFileName(WindowInfo.ReadProcess(processId).Path),
            className,
            WindowInfo.ReadTitle(hwnd),
            (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE),
            (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE),
            User32.IsWindowVisible(hwnd),
            TopLevelWindows.IsCloaked(hwnd));
    }

    /// <summary>
    /// Sets every part <paramref name="attributes"/> has. False when DWM refused one, as it does for a window that's
    /// gone or, perhaps, one of an app running as administrator.
    /// </summary>
    public static bool Apply(nint hwnd, FrameAttributes attributes)
    {
        bool accepted = true;
        // The policy first: with DWM's frame off, the rest has nothing to show on.
        if (attributes.BasicFrame)
            accepted &= Set(hwnd, Dwmapi.DWMWA_NCRENDERING_POLICY, Dwmapi.DWMNCRP_DISABLED);
        // Dark mode before the backdrop, which takes its light or dark look from it.
        if (attributes.DarkMode is { } dark)
            accepted &= Set(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        if (attributes.Backdrop is { } backdrop)
            accepted &= Set(hwnd, Dwmapi.DWMWA_SYSTEMBACKDROP_TYPE, (int)backdrop);
        if (attributes.CaptionColor is { } caption)
            accepted &= Set(hwnd, Dwmapi.DWMWA_CAPTION_COLOR, (int)ToColorRef(caption));
        if (attributes.TextColor is { } text)
            accepted &= Set(hwnd, Dwmapi.DWMWA_TEXT_COLOR, (int)ToColorRef(text));
        if (attributes.BorderColor is { } border)
            accepted &= Set(hwnd, Dwmapi.DWMWA_BORDER_COLOR, (int)ToColorRef(border));
        if (attributes.Corners is { } corners)
            accepted &= Set(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, (int)corners);
        return accepted;
    }

    /// <summary>
    /// Puts <paramref name="parts"/> back to Windows' defaults, and dark mode to <paramref name="darkMode"/> (what the
    /// window had before, from <see cref="ReadDarkMode"/>). Colours can't be read back, so an app's own caption colour
    /// becomes Windows' until the app sets it again.
    /// </summary>
    public static bool Reset(nint hwnd, FrameParts parts, bool darkMode)
    {
        const int DWMWA_COLOR_DEFAULT = unchecked((int)Dwmapi.DWMWA_COLOR_DEFAULT);
        bool accepted = true;
        if (parts.HasFlag(FrameParts.Backdrop))
            accepted &= Set(hwnd, Dwmapi.DWMWA_SYSTEMBACKDROP_TYPE, Dwmapi.DWMSBT_AUTO);
        if (parts.HasFlag(FrameParts.DarkMode))
            accepted &= Set(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, darkMode ? 1 : 0);
        if (parts.HasFlag(FrameParts.CaptionColor))
            accepted &= Set(hwnd, Dwmapi.DWMWA_CAPTION_COLOR, DWMWA_COLOR_DEFAULT);
        if (parts.HasFlag(FrameParts.TextColor))
            accepted &= Set(hwnd, Dwmapi.DWMWA_TEXT_COLOR, DWMWA_COLOR_DEFAULT);
        if (parts.HasFlag(FrameParts.BorderColor))
            accepted &= Set(hwnd, Dwmapi.DWMWA_BORDER_COLOR, DWMWA_COLOR_DEFAULT);
        if (parts.HasFlag(FrameParts.Corners))
            accepted &= Set(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, Dwmapi.DWMWCP_DEFAULT);
        if (parts.HasFlag(FrameParts.BasicFrame))
            accepted &= Set(hwnd, Dwmapi.DWMWA_NCRENDERING_POLICY, Dwmapi.DWMNCRP_USEWINDOWSTYLE);
        return accepted;
    }

    /// <summary>Whether the window's frame is dark now; null when DWM won't say (the window is gone).</summary>
    public static bool? ReadDarkMode(nint hwnd)
    {
        int dark = 0;
        return Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int)) == 0 ? dark != 0 : null;
    }

    /// <summary>0xAARRGGBB as DWM's <c>COLORREF</c> (0x00BBGGRR); a transparent colour as <c>DWMWA_COLOR_NONE</c>.</summary>
    internal static uint ToColorRef(uint argb) =>
        argb >> 24 == 0 ? Dwmapi.DWMWA_COLOR_NONE : ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);

    private static bool Set(nint hwnd, uint attribute, int value) =>
        Dwmapi.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(int)) == 0;
}
