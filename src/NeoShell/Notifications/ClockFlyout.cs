using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace NeoShell.Notifications;

/// <summary>
/// What the taskbar clock opens, as in Windows 11: the notification center above the calendar, two acrylic panels
/// of their own at the right of the screen, sliding in from its edge together.
/// </summary>
/// <remarks>
/// Two windows rather than one flyout, because each panel has its own acrylic with the desktop showing between them;
/// a flyout has one backdrop for its whole window. Either can have the focus; the flyout closes once neither has.
/// </remarks>
internal sealed class ClockFlyout : IDisposable
{
    // Effective pixels, measured on Explorer's.
    private const double PanelWidth = 336;
    private const double Gap = 12;
    private const double TopMargin = 8;
    private static readonly TimeSpan s_openDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_closeDuration = TimeSpan.FromMilliseconds(150);

    private readonly NotificationCenter _center;
    private readonly NotificationPanel _notificationPanel;
    private readonly CalendarPanel _calendarPanel;
    private readonly PanelWindow _notifications;
    private readonly PanelWindow _calendar;
    private (DisplayMonitor Monitor, RectInt32 Taskbar) _anchor;
    private nint _previousForeground;
    private long _deactivatedAt;

    public ClockFlyout(NotificationCenter center, FocusSession focus, SettingsStore settings, RunMode runMode)
    {
        _center = center;
        _notificationPanel = new NotificationPanel(center, runMode);
        _calendarPanel = new CalendarPanel(settings, focus);
        _notifications = new PanelWindow(_notificationPanel, activates: true);
        _calendar = new PanelWindow(_calendarPanel, activates: true);

        _notificationPanel.ContentResized += Relayout;
        _notificationPanel.CloseRequested += Hide;
        _calendarPanel.ContentResized += Relayout;
        center.Changed += OnNotificationsChanged;
        center.DoNotDisturbChanged += OnNotificationsChanged;
        foreach (PanelWindow window in (PanelWindow[])[_notifications, _calendar])
        {
            window.Activated += Window_Activated;
            ((UIElement)window.Content).AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Content_KeyDown), handledEventsToo: true);
        }
    }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Clicking the clock deactivates the flyout before the click arrives; that click must not reopen it.
    /// </summary>
    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    public void Dispose()
    {
        _center.Changed -= OnNotificationsChanged;
        _center.DoNotDisturbChanged -= OnNotificationsChanged;
        IsOpen = false;
        _notifications.Close();
        _calendar.Close();
    }

    /// <summary>Opens above <paramref name="taskbar"/> at the right of <paramref name="monitor"/>.</summary>
    /// <param name="accent">The panels' colour when Windows shows the accent colour on Start and taskbar.</param>
    public void Show(DisplayMonitor monitor, RectInt32 taskbar, ElementTheme theme, Color? accent)
    {
        _anchor = (monitor, taskbar);
        _notifications.SetTheme(theme, accent);
        _calendar.SetTheme(theme, accent);
        _notificationPanel.Opening();
        _calendarPanel.Opening();

        IsOpen = true;
        _previousForeground = TopLevelWindows.GetForeground();
        int offScreen = monitor.Bounds.X + monitor.Bounds.Width;
        (RectInt32 notifications, RectInt32 calendar) = Layout();
        _notifications.SlideIn(notifications, offScreen, s_openDuration);
        _calendar.SlideIn(calendar, offScreen, s_openDuration);
        // The click on the taskbar (or Win+N) was the last input, so the flyout may take the foreground.
        TopLevelWindows.Activate(_calendar.Handle);
        _calendar.Activate();
    }

    public void Hide() => Hide(restoreForeground: true);

    private void Hide(bool restoreForeground)
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        nint foreground = TopLevelWindows.GetForeground();
        if (restoreForeground && IsOwn(foreground) && _previousForeground != 0 && TopLevelWindows.Exists(_previousForeground))
            TopLevelWindows.Activate(_previousForeground);
        int offScreen = _anchor.Monitor.Bounds.X + _anchor.Monitor.Bounds.Width;
        _notifications.SlideOut(offScreen, s_closeDuration);
        _calendar.SlideOut(offScreen, s_closeDuration);
    }

    private bool IsOwn(nint window) => window == _notifications.Handle || window == _calendar.Handle;

    // Bottom up: the calendar the gap above the taskbar, the notification center the gap above that, as tall as its
    // notifications need, up to the top of the screen.
    private (RectInt32 Notifications, RectInt32 Calendar) Layout()
    {
        RectInt32 screen = _anchor.Monitor.Bounds;
        double scale = _anchor.Monitor.Dpi / 96.0;
        int width = Pixels(PanelWidth, scale);
        int x = screen.X + screen.Width - Pixels(Gap, scale) - width;

        int calendarHeight = Pixels(_calendar.MeasureHeight(PanelWidth, double.PositiveInfinity), scale);
        int calendarY = _anchor.Taskbar.Y - Pixels(Gap, scale) - calendarHeight;
        int top = screen.Y + Pixels(TopMargin, scale);
        double room = Math.Max(0, (calendarY - Pixels(Gap, scale) - top) / scale);
        int notificationsHeight = Pixels(_notifications.MeasureHeight(PanelWidth, room), scale);
        int notificationsY = calendarY - Pixels(Gap, scale) - notificationsHeight;
        return (new RectInt32(x, notificationsY, width, notificationsHeight), new RectInt32(x, calendarY, width, calendarHeight));
    }

    private static int Pixels(double effective, double scale) => (int)Math.Ceiling(effective * scale);

    private void Relayout()
    {
        if (!IsOpen)
            return;

        (RectInt32 notifications, RectInt32 calendar) = Layout();
        _notifications.Place(notifications);
        _calendar.Place(calendar);
    }

    private void OnNotificationsChanged()
    {
        if (IsOpen)
            _notificationPanel.Refresh();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated || !IsOpen)
            return;

        // Focus moving from one panel to the other comes as a deactivation too; the foreground is known a moment later.
        _deactivatedAt = Environment.TickCount64;
        _notifications.DispatcherQueue.Post(() =>
        {
            if (!IsOwn(TopLevelWindows.GetForeground()))
                Hide(restoreForeground: false);
        });
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
            Hide();
    }
}
