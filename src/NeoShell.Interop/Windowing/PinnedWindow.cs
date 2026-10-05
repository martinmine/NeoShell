using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public enum PinnedLayer
{
    /// <summary>Below every other window (the wallpaper).</summary>
    Bottom,

    /// <summary>In the topmost band (the taskbar).</summary>
    Topmost,

    /// <summary>Among ordinary windows, where it was put last (the taskbar behind a full-screen app).</summary>
    Normal,
}

/// <summary>
/// Keeps a window at fixed bounds and in a fixed layer of the z-order, whatever else tries to move, resize or
/// raise it (activation, WinUI's handling of DPI changes, other apps).
/// </summary>
public sealed unsafe class PinnedWindow : IDisposable
{
    private readonly nint _hwnd;
    private readonly WindowSubclass _subclass;
    private PinnedLayer _layer;
    private nint _above;
    private RectInt32 _bounds;
    private int? _visibleBottom;
    // The visible part's size while the window is cut off, so an unchanged region isn't set again.
    private (int Width, int Height)? _region;

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
            ApplyRegion();
        }
    }

    /// <summary>
    /// The screen row the window is cut off at: what's below it isn't shown, as a panel sliding out of the taskbar
    /// shows only what's above the taskbar's edge. Null shows all of it.
    /// </summary>
    public int? VisibleBottom
    {
        get => _visibleBottom;
        set
        {
            _visibleBottom = value;
            ApplyRegion();
        }
    }

    public PinnedLayer Layer => _layer;

    /// <summary>
    /// Moves the window to another layer, just below <paramref name="above"/> when given, so that window covers it
    /// (<see cref="PinnedLayer.Topmost"/> and <see cref="PinnedLayer.Normal"/>).
    /// </summary>
    public void SetLayer(PinnedLayer layer, nint above = 0)
    {
        _layer = layer;
        _above = layer == PinnedLayer.Topmost ? above : 0;
        if (layer != PinnedLayer.Normal)
        {
            Apply();
            return;
        }

        // Leaving the topmost band takes HWND_NOTOPMOST; that puts the window at the top of the normal band.
        const uint keepPlace = User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE;
        User32.SetWindowPos(_hwnd, User32.HWND_NOTOPMOST, 0, 0, 0, 0, keepPlace);
        if (above != 0)
            User32.SetWindowPos(_hwnd, above, 0, 0, 0, 0, keepPlace);
    }

    public void Dispose() => _subclass.Dispose();

    // Below a topmost window is still in the topmost band. The window above may have gone since.
    private nint InsertAfter =>
        _layer == PinnedLayer.Bottom ? User32.HWND_BOTTOM
        : _above != 0 && User32.IsWindow(_above) ? _above
        : User32.HWND_TOPMOST;

    private void Apply()
    {
        uint flags = User32.SWP_NOACTIVATE | (_layer == PinnedLayer.Normal ? User32.SWP_NOZORDER : 0);
        User32.SetWindowPos(_hwnd, InsertAfter, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, flags);
    }

    private void ApplyRegion()
    {
        (int, int)? region = _visibleBottom is { } bottom && bottom < _bounds.Y + _bounds.Height
            ? (_bounds.Width, Math.Max(0, bottom - _bounds.Y))
            : null;
        if (region == _region)
            return;

        _region = region;
        // The system owns a region once it's set, and deletes it.
        User32.SetWindowRgn(_hwnd, region is (int width, int height) ? Gdi32.CreateRectRgn(0, 0, width, height) : 0, true);
    }

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
            // when its z-order is actually changing; a normal one goes wherever it's put.
            if (_layer == PinnedLayer.Bottom || (_layer == PinnedLayer.Topmost && (position->flags & User32.SWP_NOZORDER) == 0))
            {
                position->hwndInsertAfter = InsertAfter;
                position->flags &= ~User32.SWP_NOZORDER;
            }
        }
        return null;
    }
}
