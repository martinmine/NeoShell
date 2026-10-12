using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

public enum OverlayMouse { Move, Leave, Press, Release, DoubleClick, RightClick }

/// <summary>
/// A layered popup owned by another app's window (any process, any integrity level at or below this one's), showing
/// premultiplied pixels. Being owned, it stays above its owner as the owner moves in the z-order and hides with it.
/// Create, use and dispose it on one thread, which runs a message loop (<see cref="MessageLoop"/>).
/// </summary>
/// <remarks>
/// Owning a window of another thread joins the two threads' input queues for good (as with <c>AttachThreadInput</c>):
/// give each app thread its own overlay thread, so one hung app can't stall the others' input.
/// </remarks>
public sealed unsafe class OverlayWindow : IDisposable
{
    private const string ClassName = "NeoShell.FrameOverlay";

    private readonly MessageWindow _window;
    private bool _trackingLeave;

    /// <param name="clickThrough">The pointer passes through to the window below, the owner's caption or border.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The owner can't be owned (access denied).</exception>
    public OverlayWindow(nint owner, bool clickThrough)
    {
        uint exStyle = User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE
            | (clickThrough ? User32.WS_EX_TRANSPARENT : 0);
        // A popup's parent is its owner.
        _window = new MessageWindow(ClassName, OnMessage, owner, User32.WS_POPUP, exStyle, User32.CS_DBLCLKS);
    }

    public nint Handle => _window.Handle;

    public bool IsVisible { get; private set; }

    /// <summary>The pointer over a window that isn't click-through; the point is in the window's pixels.</summary>
    public event Action<OverlayMouse, PointInt32>? Mouse;

    /// <summary>Shows <paramref name="pixels"/> (premultiplied 0xAARRGGBB, top-down rows) at <paramref name="bounds"/>.</summary>
    public void Paint(ReadOnlySpan<uint> pixels, RectInt32 bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Hide();
            return;
        }

        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = bounds.Width,
            biHeight = -bounds.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        nint screen = User32.GetDC(0);
        nint memory = Gdi32.CreateCompatibleDC(screen);
        nint bitmap = Gdi32.CreateDIBSection(memory, &header, Gdi32.DIB_RGB_COLORS, out void* bits, 0, 0);
        if (bitmap != 0)
        {
            pixels[..(bounds.Width * bounds.Height)].CopyTo(new Span<uint>(bits, bounds.Width * bounds.Height));
            nint old = Gdi32.SelectObject(memory, bitmap);
            var position = new User32.POINT { x = bounds.X, y = bounds.Y };
            var size = new User32.SIZE { cx = bounds.Width, cy = bounds.Height };
            var source = new User32.POINT();
            const byte AC_SRC_ALPHA = 1;
            var blend = new User32.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            User32.UpdateLayeredWindow(Handle, screen, &position, &size, memory, &source, 0, &blend, User32.ULW_ALPHA);
            Gdi32.SelectObject(memory, old);
            Gdi32.DeleteObject(bitmap);
        }
        Gdi32.DeleteDC(memory);
        User32.ReleaseDC(0, screen);
        Show();
    }

    /// <summary>Moves without drawing again, for an owner that moved without changing size.</summary>
    public void Move(int x, int y) =>
        User32.SetWindowPos(Handle, 0, x, y, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);

    /// <summary>Moves off screen, where it may show without being seen.</summary>
    public void Park() => Move(-32000, -32000);

    public void Hide()
    {
        if (!IsVisible)
            return;
        User32.ShowWindow(Handle, User32.SW_HIDE);
        IsVisible = false;
    }

    /// <summary>Puts this window right below <paramref name="above"/> in the z-order (0: the top), leaving its owner be.</summary>
    public void PlaceBelow(nint above) =>
        User32.SetWindowPos(Handle, above, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);

    /// <summary>Keeps the pointer's input while a button is held, even off the window.</summary>
    public void Capture(bool capture)
    {
        if (capture)
            User32.SetCapture(Handle);
        else
            User32.ReleaseCapture();
    }

    public void Dispose() => _window.Dispose();

    private void Show()
    {
        if (IsVisible)
            return;
        User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);
        IsVisible = true;
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case User32.WM_MOUSEACTIVATE:
                return User32.MA_NOACTIVATE;
            case User32.WM_SETCURSOR:
                User32.SetCursor(User32.LoadCursor(0, User32.IDC_ARROW));
                return 1;
            case User32.WM_SHOWWINDOW when lParam == User32.SW_PARENTCLOSING:
                // The owner is being minimized. The system shows owned windows again the moment it restores, before
                // its restore animation ends, which leaves a frame standing empty: park off screen until drawn again.
                Park();
                return null;
            case User32.WM_MOUSEMOVE:
                if (!_trackingLeave)
                {
                    var track = new User32.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(User32.TRACKMOUSEEVENT),
                        dwFlags = User32.TME_LEAVE,
                        hwndTrack = Handle,
                    };
                    _trackingLeave = User32.TrackMouseEvent(ref track);
                }
                return Raise(OverlayMouse.Move, lParam);
            case User32.WM_MOUSELEAVE:
                _trackingLeave = false;
                return Raise(OverlayMouse.Leave, lParam);
            case User32.WM_LBUTTONDOWN:
                return Raise(OverlayMouse.Press, lParam);
            case User32.WM_LBUTTONUP:
                return Raise(OverlayMouse.Release, lParam);
            case User32.WM_LBUTTONDBLCLK:
                return Raise(OverlayMouse.DoubleClick, lParam);
            case User32.WM_RBUTTONUP:
                return Raise(OverlayMouse.RightClick, lParam);
            default:
                return null;
        }
    }

    private nint? Raise(OverlayMouse kind, nint lParam)
    {
        Mouse?.Invoke(kind, new PointInt32((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
        return 0;
    }
}
