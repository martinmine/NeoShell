using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Interop.Tray;

/// <summary>
/// The notification area's endpoint: a hidden <c>Shell_TrayWnd</c> window (with its <c>TrayNotifyWnd</c> child), the
/// window <c>Shell_NotifyIcon</c> looks up by class name and sends icons to with <c>WM_COPYDATA</c>. Only one can
/// exist usefully, so only the shell creates it. Create it on the UI thread; the callbacks run there, while the
/// calling app waits.
/// </summary>
public sealed unsafe class TrayHost : IDisposable
{
    private const uint WM_COPYDATA = 0x004A;
    private const nint TrayData = 1;
    private const nint IconRectRequest = 3;

    private readonly Func<NotifyIconData, bool> _onCommand;
    private readonly Func<NotifyIconRectRequest, RectInt32?> _onRectRequest;
    private readonly MessageWindow _trayWindow;
    private readonly MessageWindow _notifyWindow;

    /// <param name="onCommand">Applies an icon command; returns whether it succeeded, which the caller sees.</param>
    /// <param name="onRectRequest">The icon's place on screen, or null if it isn't known.</param>
    public TrayHost(Func<NotifyIconData, bool> onCommand, Func<NotifyIconRectRequest, RectInt32?> onRectRequest)
    {
        _onCommand = onCommand;
        _onRectRequest = onRectRequest;
        _trayWindow = new MessageWindow("Shell_TrayWnd", OnMessage, parent: 0, User32.WS_POPUP, User32.WS_EX_TOOLWINDOW);
        _notifyWindow = new MessageWindow("TrayNotifyWnd", (_, _, _) => null, _trayWindow.Handle, User32.WS_CHILD, 0);

        // Apps already running re-add their icons when told the taskbar was created.
        User32.SendNotifyMessage(User32.HWND_BROADCAST, User32.RegisterWindowMessage("TaskbarCreated"), 0, 0);
    }

    /// <summary>True when another process, normally Explorer, owns the notification area.</summary>
    public static bool IsTrayRunning() => User32.FindWindowEx(0, 0, "Shell_TrayWnd", null) != 0;

    /// <summary>
    /// Places the hidden window over the taskbar: apps read <c>Shell_TrayWnd</c>'s rectangle to learn where the
    /// taskbar is, e.g. to place their popups next to it.
    /// </summary>
    public void SetTaskbarBounds(RectInt32 bounds) =>
        User32.SetWindowPos(_trayWindow.Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);

    public void Dispose()
    {
        _notifyWindow.Dispose();
        _trayWindow.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WM_COPYDATA || lParam == 0)
            return null;

        var copyData = (User32.COPYDATASTRUCT*)lParam;
        var data = new ReadOnlySpan<byte>((void*)copyData->lpData, (int)copyData->cbData);
        switch (copyData->dwData)
        {
            case TrayData:
                return NotifyIconData.Parse(data) is { } icon && _onCommand(icon) ? 1 : 0;
            case IconRectRequest:
                return NotifyIconRectRequest.Parse(data) is { } request && _onRectRequest(request) is { } bounds
                    ? request.Reply(bounds)
                    : 0;
            default:
                // dwData 0 is SHAppBarMessage, which Explorer serves for other app bars; NeoShell doesn't (yet).
                return 0;
        }
    }
}
