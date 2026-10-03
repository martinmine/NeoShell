using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Keeps a window at the bottom of the z-order, covering fixed bounds, whatever else tries to move, resize or
/// raise it (activation, DPI changes, other apps). Used for the wallpaper.
/// </summary>
public sealed unsafe class BottomWindow : IDisposable
{
    private readonly nint _hwnd;
    private readonly WindowSubclass _subclass;
    private readonly RectInt32 _bounds;

    public BottomWindow(nint hwnd, RectInt32 bounds)
    {
        _hwnd = hwnd;
        _bounds = bounds;
        _subclass = new WindowSubclass(hwnd, OnMessage);
        User32.SetWindowPos(hwnd, User32.HWND_BOTTOM, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            User32.SWP_NOACTIVATE);
    }

    public void Dispose() => _subclass.Dispose();

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == User32.WM_WINDOWPOSCHANGING)
        {
            var position = (User32.WINDOWPOS*)lParam;
            position->hwndInsertAfter = User32.HWND_BOTTOM;
            position->x = _bounds.X;
            position->y = _bounds.Y;
            position->cx = _bounds.Width;
            position->cy = _bounds.Height;
            position->flags &= ~(User32.SWP_NOZORDER | User32.SWP_NOMOVE | User32.SWP_NOSIZE);
        }
        return null;
    }
}
