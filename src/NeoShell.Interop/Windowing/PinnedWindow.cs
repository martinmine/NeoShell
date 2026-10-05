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

    /// <summary>
    /// Just above the desktop and below every app's window, even when clicked (the widgets, as Vista's gadgets).
    /// </summary>
    Desktop,
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
    // The region set, so an unchanged one isn't set again.
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
            // Less of the window shows before it moves, more only after: between the two DWM may draw a frame, which
            // then never shows more than the visible part (Start sliding back into the taskbar).
            bool shrinking = (RegionFor(value)?.Height ?? value.Height) < (_region?.Height ?? _bounds.Height);
            _bounds = value;
            if (shrinking)
                ApplyRegion();
            Apply();
            if (!shrinking)
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
        : _layer == PinnedLayer.Desktop ? AboveDesktop()
        : _above != 0 && User32.IsWindow(_above) ? _above
        : User32.HWND_TOPMOST;

    /// <summary>
    /// The lowest window that's an app's: going in just below it puts this one above the desktop (Explorer's, or
    /// NeoShell's wallpaper windows, which can't simply be gone above: each puts itself at the very bottom). NeoShell's
    /// other desktop-level windows don't count, so the widgets don't push each other around.
    /// </summary>
    private nint AboveDesktop()
    {
        uint ownProcess = (uint)Environment.ProcessId;
        for (nint hwnd = User32.GetWindow(_hwnd, User32.GW_HWNDLAST); hwnd != 0; hwnd = User32.GetWindow(hwnd, User32.GW_HWNDPREV))
        {
            if (hwnd == _hwnd || !User32.IsWindowVisible(hwnd) || TopLevelWindows.IsDesktop(hwnd))
                continue;
            // Only app windows are above: going below a topmost one would make this one topmost too.
            if ((User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE) & User32.WS_EX_TOPMOST) != 0)
                return User32.HWND_TOP;
            User32.GetWindowThreadProcessId(hwnd, out uint process);
            if (process != ownProcess)
                return hwnd;
        }
        return User32.HWND_TOP;
    }

    private void Apply()
    {
        uint flags = User32.SWP_NOACTIVATE | (_layer == PinnedLayer.Normal ? User32.SWP_NOZORDER : 0);
        User32.SetWindowPos(_hwnd, InsertAfter, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, flags);
    }

    // The visible part's size while the window is cut off at VisibleBottom; null when all of it shows.
    private (int Width, int Height)? RegionFor(RectInt32 bounds) =>
        _visibleBottom is { } bottom && bottom < bounds.Y + bounds.Height
            ? (bounds.Width, Math.Max(0, bottom - bounds.Y))
            : null;

    private void ApplyRegion()
    {
        (int Width, int Height)? region = RegionFor(_bounds);
        if (region == _region)
            return;

        bool wasCutOff = _region is not null;
        _region = region;
        WindowRegion.SetVisibleHeight(_hwnd, _bounds.Width, region?.Height);

        // A region doesn't cut off Windows 11's border and shadow: their edge would cross a see-through taskbar as a line
        // and a dark band. A square window has neither. The windows cut off are rounded popups (Start, thumbnails).
        bool cutOff = region is not null;
        if (cutOff == wasCutOff)
            return;
        int corners = cutOff ? Dwmapi.DWMWCP_DONOTROUND : Dwmapi.DWMWCP_ROUND;
        Dwmapi.DwmSetWindowAttribute(_hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(int));
        uint borderColor = cutOff ? Dwmapi.DWMWA_COLOR_NONE : Dwmapi.DWMWA_COLOR_DEFAULT;
        Dwmapi.DwmSetWindowAttribute(_hwnd, Dwmapi.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
        // DWM takes these a frame later than a move: the border would still cross the taskbar as the window moves
        // into it.
        if (cutOff)
            Dwmapi.DwmFlush();
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

            // The wallpaper goes back to the bottom on every change; a topmost or desktop-level window only needs its
            // layer kept when its z-order is actually changing (it's clicked); a normal one goes wherever it's put.
            if (_layer == PinnedLayer.Bottom
                || ((_layer is PinnedLayer.Topmost or PinnedLayer.Desktop) && (position->flags & User32.SWP_NOZORDER) == 0))
            {
                position->hwndInsertAfter = InsertAfter;
                position->flags &= ~User32.SWP_NOZORDER;
            }
        }
        return null;
    }
}
