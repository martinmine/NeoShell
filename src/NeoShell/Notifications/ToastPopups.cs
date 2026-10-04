using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Notifications;

/// <summary>
/// New notifications as toasts at the bottom right of the primary monitor, while NeoShell is the shell: Windows
/// draws none without Explorer (its toasts belong to the ShellExperienceHost that Explorer runs), though it still
/// stores the notifications. Alongside Explorer, Explorer shows them.
/// </summary>
/// <remarks>
/// The newest at the bottom, the older ones pushed up; each slides in from the screen's edge and out again after
/// the system's notification time (longer while the pointer is on it). Closing one only puts it away: the
/// notification stays in the notification center, as Explorer's do.
/// </remarks>
internal sealed class ToastPopups : IDisposable
{
    // Effective pixels, measured on Explorer's.
    private const double ToastWidth = 364;
    private const double Gap = 12;
    private const double RightGap = 16;
    private const double Spacing = 8;
    private const int MaxShown = 3;
    private static readonly TimeSpan s_slideIn = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_slideOut = TimeSpan.FromMilliseconds(150);

    private readonly NotificationCenter _center;
    private readonly Func<(DisplayMonitor Monitor, RectInt32 Taskbar)?> _anchor;
    private readonly Func<(ElementTheme Theme, Color? Accent)> _theme;
    private readonly Func<bool> _suppressed;
    private readonly RunMode _runMode;
    // Oldest first.
    private readonly List<Toast> _shown = [];

    /// <param name="anchor">The primary monitor and its taskbar, the toasts go above it.</param>
    /// <param name="suppressed">True while toasts shouldn't show (the notification center is open).</param>
    public ToastPopups(
        NotificationCenter center,
        RunMode runMode,
        Func<(DisplayMonitor Monitor, RectInt32 Taskbar)?> anchor,
        Func<(ElementTheme Theme, Color? Accent)> theme,
        Func<bool> suppressed)
    {
        _center = center;
        _runMode = runMode;
        _anchor = anchor;
        _theme = theme;
        _suppressed = suppressed;
        center.Arrived += Show;
        center.Changed += OnNotificationsChanged;
    }

    public void Dispose()
    {
        _center.Arrived -= Show;
        _center.Changed -= OnNotificationsChanged;
        foreach (Toast toast in _shown)
        {
            toast.Timer.Stop();
            toast.Window.Close();
        }
        _shown.Clear();
    }

    private sealed record Toast(PanelWindow Window, NotificationCard Card, DispatcherQueueTimer Timer);

    private void Show(ToastInfo info)
    {
        if (_suppressed() || _anchor() is not { } anchor)
            return;

        var card = new NotificationCard(info, isToast: true);
        SetLogo(card, info.AppId);
        var window = new PanelWindow(card, activates: false);
        (ElementTheme theme, Color? accent) = _theme();
        window.SetTheme(theme, accent);

        DispatcherQueueTimer timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = UserNotifications.PopupDuration;
        timer.IsRepeating = false;
        var toast = new Toast(window, card, timer);
        timer.Tick += (_, _) =>
        {
            // Not from under the pointer: it waits until the pointer leaves.
            if (!card.IsPointerOver)
                Dismiss(toast);
        };
        card.PointerOverChanged += () =>
        {
            if (!card.IsPointerOver)
                timer.Start();
        };
        card.CloseRequested += _ => Dismiss(toast);
        card.Invoked += _ =>
        {
            NotificationPanel.Open(info);
            _center.Remove([info]);
            Dismiss(toast);
        };
        card.TurnOffRequested += _ =>
        {
            _center.TurnOff(info.AppId);
            Dismiss(toast);
        };
        card.SettingsRequested += () => Launcher.OpenSettings(_runMode, "Notifications", "ms-settings:notifications");

        _shown.Add(toast);
        while (_shown.Count > MaxShown)
            Dismiss(_shown[0]);

        double scale = anchor.Monitor.Dpi / 96.0;
        RectInt32 screen = anchor.Monitor.Bounds;
        int width = (int)Math.Ceiling(ToastWidth * scale);
        int height = (int)Math.Ceiling(window.MeasureHeight(ToastWidth, double.PositiveInfinity) * scale);
        window.SlideIn(Stack(new RectInt32(0, 0, width, height), toast), screen.X + screen.Width, s_slideIn);
        Restack(except: toast);
        timer.Start();
        Log.Info($"Toast from {info.AppId}");
    }

    private async void SetLogo(NotificationCard card, string appId) => card.Logo = await _center.GetLogoAsync(appId);

    // The toasts above the taskbar, the newest lowest; those already shown slide to their places.
    private void Restack(Toast? except = null)
    {
        foreach (Toast toast in _shown.Where(t => t.Window.IsShown && t != except))
            toast.Window.Place(Stack(toast.Window.ScreenBounds, toast), s_slideIn);
    }

    private RectInt32 Stack(RectInt32 bounds, Toast toast)
    {
        if (_anchor() is not { } anchor)
            return bounds;

        double scale = anchor.Monitor.Dpi / 96.0;
        int y = anchor.Taskbar.Y - (int)Math.Ceiling(Gap * scale);
        for (int i = _shown.Count - 1; i >= 0 && _shown[i] != toast; i--)
            y -= _shown[i].Window.ScreenBounds.Height + (int)Math.Ceiling(Spacing * scale);
        // Where it ends up, though it may still be sliding in.
        int x = anchor.Monitor.Bounds.X + anchor.Monitor.Bounds.Width - (int)Math.Ceiling(RightGap * scale) - bounds.Width;
        return bounds with { X = x, Y = y - bounds.Height };
    }

    private void Dismiss(Toast toast)
    {
        if (!_shown.Remove(toast))
            return;

        toast.Timer.Stop();
        RectInt32 bounds = toast.Window.ScreenBounds;
        toast.Window.SlideOut(bounds.X + bounds.Width * 2, s_slideOut, toast.Window.Close);
        Restack();
    }

    // A toast cleared from the notification center (or by its app) goes too.
    private void OnNotificationsChanged()
    {
        foreach (Toast toast in _shown.Where(t => !_center.IsStored(t.Card.Toast.Id)).ToList())
            Dismiss(toast);
    }
}
