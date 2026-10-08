using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// An invisible window over part of the screen that takes clicks without activating or passing them on: a popup that
/// closes on a click elsewhere puts it just below itself, so that click only closes it (as Explorer's sticky Alt+Tab,
/// whose window covers the work area). Create it on the UI thread; <see cref="Clicked"/> is raised there.
/// </summary>
public sealed class ClickCatcher : IDisposable
{
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_XBUTTONDOWN = 0x020B;
    private const nint MA_NOACTIVATE = 3;

    private readonly MessageWindow _window;

    public ClickCatcher()
    {
        _window = new MessageWindow("NeoShellClickCatcher", OnMessage, parent: 0, User32.WS_POPUP,
            User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE);
        // All but transparent: a fully transparent layered window lets clicks through.
        User32.SetLayeredWindowAttributes(_window.Handle, 0, 1, User32.LWA_ALPHA);
    }

    /// <summary>A mouse button went down on it.</summary>
    public event Action? Clicked;

    /// <summary>Covers <paramref name="bounds"/> (screen pixels) just below <paramref name="popup"/>, a topmost window.</summary>
    public void Show(RectInt32 bounds, nint popup) =>
        User32.SetWindowPos(_window.Handle, popup, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW);

    public void Hide() => User32.ShowWindow(_window.Handle, User32.SW_HIDE);

    public void Dispose() => _window.Dispose();

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN:
                Clicked?.Invoke();
                return 0;
            default:
                return null;
        }
    }
}
