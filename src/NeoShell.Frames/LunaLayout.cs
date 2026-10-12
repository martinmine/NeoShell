using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Frames;

/// <summary>
/// Where a window's Luna frame goes on screen, in physical pixels: XP's window is the client area with XP's borders
/// around it (4 px, 3 for dialogs) and the caption from the top of the visible frame to the bottom of the title bar.
/// The borders fall in Windows 11's invisible resize border, so they never cover the app's content and resizing still
/// works through them.
/// </summary>
/// <param name="Caption">The caption band; its width is XP's window width.</param>
/// <param name="Left">The left border below the caption, down to the bottom border; empty when maximized.</param>
/// <param name="Buttons">The part of the caption that takes the pointer: from Windows' own caption buttons, which it
/// keeps the pointer off, to the right end.</param>
internal sealed record LunaLayout(LunaKind Kind, bool Maximized, RectInt32 Caption, RectInt32 Left, RectInt32 Right, RectInt32 Bottom,
    RectInt32 Buttons)
{
    // Fewer rows than this between the frame's top and the client area: the app draws its own title bar.
    private const int MinCaptionHeight = 10;

    /// <summary>Null when the window has no standard title bar to cover (the app draws its own, or it's full screen).</summary>
    public static LunaLayout? From(WindowCaption window, int buttonCount)
    {
        RectInt32 visible = window.Visible, client = window.Client;
        int captionBottom = window.TitleBarBottom;
        if (!window.HasTitleBar || captionBottom - visible.Y < MinCaptionHeight || client.Width <= 0)
            return null;

        LunaKind kind = window.IsResizable ? LunaKind.Sizable : LunaKind.Dialog;
        RectInt32 caption, left = default, right = default, bottom = default;
        if (window.IsMaximized)
        {
            // XP put a maximized window's borders off screen.
            caption = new RectInt32(visible.X, visible.Y, visible.Width, captionBottom - visible.Y);
        }
        else
        {
            int border = LunaFrame.BorderWidth(kind);
            int x = client.X - border, width = client.Width + 2 * border, sideHeight = client.Y + client.Height - captionBottom;
            caption = new RectInt32(x, visible.Y, width, captionBottom - visible.Y);
            left = new RectInt32(x, captionBottom, border, sideHeight);
            right = new RectInt32(client.X + client.Width, captionBottom, border, sideHeight);
            bottom = new RectInt32(x, client.Y + client.Height, width, border);
        }

        RectInt32 buttons = default;
        if (buttonCount > 0)
        {
            var probe = new LunaWindow(caption.Width, caption.Height, kind, false, window.IsMaximized, "", null,
                [.. Enumerable.Repeat((LunaButton.Close, LunaButtonState.Normal), buttonCount)]);
            int buttonsLeft = Math.Min(window.CaptionButtonsLeft, caption.X + LunaFrame.ButtonRects(probe)[0].X - 2);
            buttonsLeft = Math.Clamp(buttonsLeft, caption.X, caption.X + caption.Width);
            buttons = new RectInt32(buttonsLeft, caption.Y, caption.X + caption.Width - buttonsLeft, caption.Height);
        }
        return new LunaLayout(kind, window.IsMaximized, caption, left, right, bottom, buttons);
    }

    /// <summary>The same layout moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public LunaLayout Offset(int dx, int dy)
    {
        RectInt32 Moved(RectInt32 rect) => new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
        return this with { Caption = Moved(Caption), Left = Moved(Left), Right = Moved(Right), Bottom = Moved(Bottom), Buttons = Moved(Buttons) };
    }

    /// <summary>
    /// XP's caption buttons for the window, left to right: minimize and maximize (or restore) when it has either, the
    /// missing or disabled one greyed; help instead when it has neither; then close. <paramref name="hot"/> is the
    /// button under the pointer, <paramref name="pressed"/> the one held down (pressed only while still under it).
    /// </summary>
    public static (LunaButton Button, LunaButtonState State)[] ButtonsFor(WindowCaption window, LunaButton? hot, LunaButton? pressed)
    {
        var buttons = new List<(LunaButton, LunaButtonState)>();
        if (window.Minimize != CaptionButtonState.Hidden || window.Maximize != CaptionButtonState.Hidden)
        {
            buttons.Add((LunaButton.Minimize, State(LunaButton.Minimize, window.Minimize)));
            LunaButton maximize = window.IsMaximized ? LunaButton.Restore : LunaButton.Maximize;
            buttons.Add((maximize, State(maximize, window.Maximize)));
        }
        else if (window.Help != CaptionButtonState.Hidden)
        {
            buttons.Add((LunaButton.Help, State(LunaButton.Help, window.Help)));
        }
        if (window.Close != CaptionButtonState.Hidden)
            buttons.Add((LunaButton.Close, State(LunaButton.Close, window.Close)));
        return [.. buttons];

        LunaButtonState State(LunaButton button, CaptionButtonState state) =>
            state != CaptionButtonState.Enabled ? LunaButtonState.Disabled
            : pressed is { } held ? (held == button && hot == button ? LunaButtonState.Pressed : LunaButtonState.Normal)
            : hot == button ? LunaButtonState.Hot
            : LunaButtonState.Normal;
    }
}
