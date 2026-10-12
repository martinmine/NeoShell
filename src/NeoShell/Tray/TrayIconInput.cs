using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using NeoShell.Interop.Tray;

namespace NeoShell.Tray;

/// <summary>
/// The pointer on a taskbar's tray icons, passed on to their apps as Explorer does, whatever the icons look like:
/// presses and releases (a second left press within the double-click time is a double-click), moves over an icon, and
/// the hover that opens an app's own pop-up after 400 ms. The view adds what's its own (Windows 11's dragging).
/// </summary>
internal sealed class TrayIconInput
{
    private readonly NotificationArea _tray;
    private readonly DispatcherQueueTimer _hoverTimer;
    private TrayIcon? _hovered;
    private bool _popupOpen;
    private (TrayIcon Icon, long Time)? _lastLeftDown;

    public TrayIconInput(NotificationArea tray)
    {
        _tray = tray;
        _hoverTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(400);
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += (_, _) =>
        {
            if (_hovered is { } icon)
            {
                _popupOpen = true;
                _tray.Send(icon, TrayMouseEvent.HoverStart);
            }
        };
    }

    /// <summary>
    /// What a press on <paramref name="icon"/> tells its app, or null for a button the tray leaves alone. A left
    /// press is remembered, for a second one to make a double-click.
    /// </summary>
    public TrayMouseEvent? PressEvent(TrayIcon icon, PointerPointProperties button)
    {
        if (button.IsLeftButtonPressed)
        {
            // Windows turns a second press within the double-click time into a double-click; so does the tray.
            long now = Environment.TickCount64;
            bool doubleClick = _lastLeftDown is { } last && last.Icon == icon && now - last.Time <= NotifyIconInput.DoubleClickTime;
            _lastLeftDown = doubleClick ? null : (icon, now);
            return doubleClick ? TrayMouseEvent.LeftDoubleClick : TrayMouseEvent.LeftDown;
        }
        if (button.IsRightButtonPressed)
            return TrayMouseEvent.RightDown;
        if (button.IsMiddleButtonPressed)
            return TrayMouseEvent.MiddleDown;
        return null;
    }

    /// <summary>Tells the app of a press (<see cref="PressEvent"/>), which ends the hover.</summary>
    public void Press(TrayIcon icon, TrayMouseEvent pressed)
    {
        EndHover();
        _tray.Send(icon, pressed);
    }

    /// <summary>What letting go of a button tells the app, or null for one the tray leaves alone.</summary>
    public static TrayMouseEvent? ReleaseEvent(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonReleased => TrayMouseEvent.LeftUp,
        PointerUpdateKind.RightButtonReleased => TrayMouseEvent.RightUp,
        PointerUpdateKind.MiddleButtonReleased => TrayMouseEvent.MiddleUp,
        _ => null,
    };

    public void Release(TrayIcon icon, TrayMouseEvent released) => _tray.Send(icon, released);

    /// <summary>The pointer moved over the icon (a captured pointer that has left it doesn't count).</summary>
    public void Move(TrayIcon icon) => _tray.Send(icon, TrayMouseEvent.Move);

    /// <summary>The pointer came onto the icon: its app's pop-up opens if it stays.</summary>
    public void Enter(TrayIcon icon)
    {
        _hovered = icon;
        _hoverTimer.Start();
    }

    /// <summary>The pointer left the icon, or pressed it: an open pop-up is told to close.</summary>
    public void EndHover()
    {
        _hoverTimer.Stop();
        if (_popupOpen && _hovered is { } icon)
            _tray.Send(icon, TrayMouseEvent.HoverEnd);
        _popupOpen = false;
        _hovered = null;
    }

    /// <summary>The taskbar closed.</summary>
    public void Stop() => _hoverTimer.Stop();
}
