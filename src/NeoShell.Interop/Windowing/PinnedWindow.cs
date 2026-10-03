using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public enum PinnedLayer
{
    /// <summary>Below every other window (the wallpaper).</summary>
    Bottom,

    /// <summary>In the topmost band (the taskbar).</summary>
    Topmost,
}

/// <summary>
/// Keeps a window at fixed bounds and in a fixed layer of the z-order, whatever else tries to move, resize or
/// raise it (activation, WinUI's handling of DPI changes, other apps).
/// </summary>
public sealed unsafe class PinnedWindow : IDisposable
{
    private readonly nint _hwnd;
    private readonly PinnedLayer _layer;
    private readonly WindowSubclass _subclass;
    private RectInt32 _bounds;

    public PinnedWindow(nint hwnd, RectInt32 bounds, PinnedLayer layer)
    {
        _hwnd = hwnd;
        _layer = layer;
        _bounds = bounds;
        _subclass = new WindowSubclass(hwnd, OnMessage);
        Apply();
    }

    public RectInt32 Bounds
    {
        get => _bounds;
        set
        {
            _bounds = value;
            Apply();
        }
    }

    public void Dispose() => _subclass.Dispose();

    private nint InsertAfter => _layer == PinnedLayer.Bottom ? User32.HWND_BOTTOM : User32.HWND_TOPMOST;

    private void Apply() =>
        User32.SetWindowPos(_hwnd, InsertAfter, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, User32.SWP_NOACTIVATE);

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == User32.WM_WINDOWPOSCHANGING)
        {
            var position = (User32.WINDOWPOS*)lParam;
            position->x = _bounds.X;
            position->y = _bounds.Y;
            position->cx = _bounds.Width;
            position->cy = _bounds.Height;
            position->flags &= ~(User32.SWP_NOMOVE | User32.SWP_NOSIZE);

            // The wallpaper goes back to the bottom on every change; a topmost window only needs its layer kept
            // when its z-order is actually changing.
            if (_layer == PinnedLayer.Bottom || (position->flags & User32.SWP_NOZORDER) == 0)
            {
                position->hwndInsertAfter = InsertAfter;
                position->flags &= ~User32.SWP_NOZORDER;
            }
        }
        return null;
    }
}
