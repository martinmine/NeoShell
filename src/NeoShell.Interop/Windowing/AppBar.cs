using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Registers a window as an app bar with the shell, which reserves screen space for it so maximized windows don't
/// cover it. Only works while another shell (Explorer) is running: it is that shell that manages app bars.
/// </summary>
public sealed unsafe class AppBar : IDisposable
{
    private static readonly uint s_callbackMessage = User32.RegisterWindowMessage("NeoShell_AppBarNotify");

    private readonly nint _hwnd;
    private readonly WindowSubclass _subclass;

    public AppBar(nint hwnd)
    {
        _hwnd = hwnd;
        _subclass = new WindowSubclass(hwnd, OnMessage);

        Shell32.APPBARDATA data = NewData();
        data.uCallbackMessage = s_callbackMessage;
        if (Shell32.SHAppBarMessage(Shell32.ABM_NEW, &data) == 0)
        {
            _subclass.Dispose();
            throw new InvalidOperationException("The shell refused to register the app bar.");
        }
    }

    /// <summary>The space available may have changed (another app bar moved): dock again.</summary>
    public event Action? PositionChanged;

    /// <summary>
    /// Reserves a strip of <paramref name="height"/> pixels along the bottom of <paramref name="monitor"/>, above any
    /// app bars already there, and returns the strip granted.
    /// </summary>
    public RectInt32 DockBottom(RectInt32 monitor, int height)
    {
        Shell32.APPBARDATA data = NewData();
        data.uEdge = Shell32.ABE_BOTTOM;
        data.rc = AlignToBottom(User32.RECT.From(monitor), height);
        Shell32.SHAppBarMessage(Shell32.ABM_QUERYPOS, &data);
        // The shell may have moved the bottom edge up; the height is ours to keep.
        data.rc = AlignToBottom(data.rc, height);
        Shell32.SHAppBarMessage(Shell32.ABM_SETPOS, &data);
        return data.rc.ToRectInt32();
    }

    /// <summary>
    /// Reserves a strip of <paramref name="width"/> pixels along the right of <paramref name="area"/>, left of any
    /// app bars already there, and returns the strip granted. <paramref name="area"/> gives the strip's height: the
    /// monitor's work area, so it ends above the taskbar.
    /// </summary>
    public RectInt32 DockRight(RectInt32 area, int width)
    {
        Shell32.APPBARDATA data = NewData();
        data.uEdge = Shell32.ABE_RIGHT;
        data.rc = AlignToRight(User32.RECT.From(area), width);
        Shell32.SHAppBarMessage(Shell32.ABM_QUERYPOS, &data);
        data.rc = AlignToRight(data.rc, width);
        Shell32.SHAppBarMessage(Shell32.ABM_SETPOS, &data);
        return data.rc.ToRectInt32();
    }

    public void Dispose()
    {
        Shell32.APPBARDATA data = NewData();
        Shell32.SHAppBarMessage(Shell32.ABM_REMOVE, &data);
        _subclass.Dispose();
    }

    internal static User32.RECT AlignToBottom(User32.RECT area, int height) => area with { top = area.bottom - height };

    internal static User32.RECT AlignToRight(User32.RECT area, int width) => area with { left = area.right - width };

    private Shell32.APPBARDATA NewData() => new() { cbSize = (uint)sizeof(Shell32.APPBARDATA), hWnd = _hwnd };

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        Shell32.APPBARDATA data = NewData();
        if (message == s_callbackMessage)
        {
            if (wParam == Shell32.ABN_POSCHANGED)
                PositionChanged?.Invoke();
            return 0;
        }

        // Documented duties of an app bar, so the shell keeps its z-order right.
        if (message == User32.WM_ACTIVATE)
            Shell32.SHAppBarMessage(Shell32.ABM_ACTIVATE, &data);
        else if (message == User32.WM_WINDOWPOSCHANGED)
            Shell32.SHAppBarMessage(Shell32.ABM_WINDOWPOSCHANGED, &data);
        return null;
    }
}
