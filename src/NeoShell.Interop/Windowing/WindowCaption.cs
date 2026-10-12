using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public enum CaptionButtonState { Hidden, Enabled, Disabled }

/// <summary>
/// Where a window's title bar and frame are on screen, and the state of its caption buttons: what a frame drawn over
/// it must cover. Rectangles are in physical pixels.
/// </summary>
/// <param name="Window">The window's rectangle, invisible resize borders included.</param>
/// <param name="Visible">The frame as drawn (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>), without the invisible resize borders.</param>
/// <param name="Client">The client area.</param>
/// <param name="TitleBarBottom">The first row below the title bar: a menu bar may lie between it and the client area.</param>
/// <param name="HasTitleBar">Windows draws a standard title bar (it's not hidden by the window's style).</param>
/// <param name="CaptionButtonsLeft">The left edge of Windows' own caption buttons (<c>DWMWA_CAPTION_BUTTON_BOUNDS</c>).</param>
/// <param name="IsActive">The window draws its caption active (<c>WS_ACTIVECAPTION</c>), which can lag or be stale.</param>
public sealed record WindowCaption(
    RectInt32 Window,
    RectInt32 Visible,
    RectInt32 Client,
    int TitleBarBottom,
    bool HasTitleBar,
    int CaptionButtonsLeft,
    CaptionButtonState Minimize,
    CaptionButtonState Maximize,
    CaptionButtonState Help,
    CaptionButtonState Close,
    bool IsMaximized,
    bool IsResizable,
    bool IsActive)
{
    /// <summary>
    /// Null when the window is gone or DWM doesn't frame it. With <paramref name="like"/>, an earlier read of the window
    /// in the same maximized state, the frame's insets and caption buttons are taken from that rather than asked of DWM,
    /// whose answers can wait for its next composition while the window is being resized.
    /// </summary>
    public static unsafe WindowCaption? Read(nint hwnd, WindowCaption? like = null)
    {
        if (!User32.GetWindowRect(hwnd, out User32.RECT windowRect) || !User32.GetClientRect(hwnd, out User32.RECT client))
            return null;
        RectInt32 window = windowRect.ToRectInt32();
        bool maximized = User32.IsZoomed(hwnd);
        if (like is not null && like.IsMaximized != maximized)
            like = null;

        User32.RECT visible;
        if (like is not null)
        {
            visible = new User32.RECT
            {
                left = window.X + like.Visible.X - like.Window.X,
                top = window.Y + like.Visible.Y - like.Window.Y,
                right = window.X + window.Width - (like.Window.X + like.Window.Width - like.Visible.X - like.Visible.Width),
                bottom = window.Y + window.Height - (like.Window.Y + like.Window.Height - like.Visible.Y - like.Visible.Height),
            };
        }
        else if (Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, &visible, (uint)sizeof(User32.RECT)) != 0)
        {
            return null;
        }
        var origin = new User32.POINT();
        User32.ClientToScreen(hwnd, ref origin);

        var titleBar = new User32.TITLEBARINFO { cbSize = (uint)sizeof(User32.TITLEBARINFO) };
        bool hasTitleBar = User32.GetTitleBarInfo(hwnd, ref titleBar)
            && (titleBar.rgstate[0] & (User32.STATE_SYSTEM_INVISIBLE | User32.STATE_SYSTEM_OFFSCREEN)) == 0;
        var info = new User32.WINDOWINFO { cbSize = (uint)sizeof(User32.WINDOWINFO) };
        User32.GetWindowInfo(hwnd, ref info);

        // The caption buttons keep their place from the right edge.
        User32.RECT buttons;
        int buttonsLeft = like is not null ? window.X + window.Width - (like.Window.X + like.Window.Width - like.CaptionButtonsLeft)
            : Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CAPTION_BUTTON_BOUNDS, &buttons, (uint)sizeof(User32.RECT)) == 0
                && buttons.right > buttons.left
                ? window.X + buttons.left
                : visible.right;

        return new WindowCaption(
            window,
            visible.ToRectInt32(),
            new RectInt32(origin.x, origin.y, client.right, client.bottom),
            // rcTitleBar's bottom is its last row.
            Math.Min(titleBar.rcTitleBar.bottom + 1, origin.y),
            hasTitleBar,
            buttonsLeft,
            ButtonState(titleBar.rgstate[2]),
            ButtonState(titleBar.rgstate[3]),
            ButtonState(titleBar.rgstate[4]),
            ButtonState(titleBar.rgstate[5]),
            maximized,
            (info.dwStyle & User32.WS_THICKFRAME) != 0,
            (info.dwWindowStatus & User32.WS_ACTIVECAPTION) != 0);
    }

    private static CaptionButtonState ButtonState(uint state) =>
        (state & User32.STATE_SYSTEM_INVISIBLE) != 0 ? CaptionButtonState.Hidden
        : (state & User32.STATE_SYSTEM_UNAVAILABLE) != 0 ? CaptionButtonState.Disabled
        : CaptionButtonState.Enabled;
}
