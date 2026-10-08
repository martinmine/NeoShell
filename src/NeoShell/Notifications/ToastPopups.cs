using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Windowing;
using NeoShell.Interop.Tray;
using NeoShell.Logging;
using NeoShell.Tray;
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
/// notification stays in the notification center, as Explorer's do; clicking it activates its app with the toast's own
/// arguments. Each plays its sound as it starts to slide in (the newest's takes over, and a toast's stops when it
/// goes). Tray icons' balloons show the same way.
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
    private readonly NotificationSound _sound = new();
    // The toast whose sound plays, if any.
    private Toast? _sounding;

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
        _sound.Dispose();
    }

    /// <param name="BalloonKey">For a tray balloon, its icon's key.</param>
    /// <param name="Completed">For a tray balloon, tells its app how it went.</param>
    private sealed record Toast(
        PanelWindow Window, NotificationCard Card, DispatcherQueueTimer Timer, string? BalloonKey, Action<BalloonEvent>? Completed);

    private void Show(ToastInfo info)
    {
        if (_suppressed() || _anchor() is null)
            return;

        var card = new NotificationCard(info, isToast: true);
        SetLogo(card, info.AppId);
        card.Invoked += _ => _center.Activate(info);
        Show(card, balloonKey: null, completed: null, NotificationCenter.SoundFor(info));
    }

    /// <summary>
    /// Shows a tray icon's balloon as a toast, in place of the icon's last one if that's still up. Returns false when
    /// it can't show (Do not disturb, the app's notifications or banners turned off, the notification center open):
    /// Explorer's toast is then dropped by the notification platform, and its app hears it timed out.
    /// </summary>
    /// <param name="completed">Tells the app how it went: clicked, or timed out (also when closed).</param>
    public bool ShowBalloon(TrayBalloon balloon, Action<BalloonEvent> completed)
    {
        Hide(balloon.IconKey);
        if (_suppressed() || _anchor() is null || !_center.ShowsBanner(balloon.AppId))
            return false;

        // Explorer's toast for it has no audio element, or a silent one for NIIF_NOSOUND.
        var info = new ToastInfo(
            0, balloon.AppId, balloon.AppName, DateTimeOffset.Now, balloon.Title, balloon.Body, balloon.Silent ? ToastAudio.None : null);
        var card = new NotificationCard(info, isToast: true);
        if (balloon.Picture is { } picture)
            card.Picture = AppIcons.ToImageSource(picture);
        if (balloon.Logo is { } logo)
            card.Logo = AppIcons.ToImageSource(logo);
        else
            SetLogo(card, balloon.AppId);
        Show(card, balloon.IconKey, completed, NotificationCenter.SoundFor(info));
        return true;
    }

    /// <summary>
    /// Shows one of Windows' own toasts that are only a banner, never kept in the notification center (AutoPlay's),
    /// in place of the last one with the same key. Returns false when it can't show, as <see cref="ShowBalloon"/>.
    /// </summary>
    /// <param name="completed">Clicked, or timed out (also when closed).</param>
    public bool ShowBanner(string key, ToastInfo info, Action<BalloonEvent> completed)
    {
        Hide(key);
        if (_suppressed() || _anchor() is null || !_center.ShowsBanner(info.AppId))
            return false;

        var card = new NotificationCard(info, isToast: true);
        // Windows' system toasts have no logo of their own: the notification UI draws its default app glyph.
        card.ShowDefaultLogo();
        Show(card, key, completed, NotificationCenter.SoundFor(info));
        return true;
    }

    /// <summary>
    /// Shows a toast that an app would show for itself under Explorer, but NeoShell shows in its place (Snipping Tool's
    /// for a snip, see <see cref="Capture.ScreenSnip"/>): the app's logo and name, a hero image and a button. Only a
    /// banner, as <see cref="ShowBanner"/>: NeoShell can't store a notification as another app.
    /// </summary>
    /// <param name="invoked">The toast was clicked: true on its button, false elsewhere.</param>
    public bool ShowAppBanner(string key, ToastInfo info, ImageSource hero, string action, Action<bool> invoked)
    {
        Hide(key);
        if (_suppressed() || _anchor() is null || !_center.ShowsBanner(info.AppId))
            return false;

        var card = new NotificationCard(info, isToast: true) { Hero = hero, ActionText = action };
        SetLogo(card, info.AppId);
        card.ActionInvoked += _ =>
        {
            Hide(key);
            invoked(true);
        };
        Show(card, key, result =>
        {
            if (result == BalloonEvent.Clicked)
                invoked(false);
        }, NotificationCenter.SoundFor(info));
        return true;
    }

    /// <summary>
    /// Takes a balloon (by its icon's key) or banner away, if it's up, without telling its app (as Explorer does).
    /// </summary>
    public void Hide(string key)
    {
        if (_shown.FirstOrDefault(t => t.BalloonKey == key) is { } toast)
            Dismiss(toast, result: null);
    }

    private void Show(NotificationCard card, string? balloonKey, Action<BalloonEvent>? completed, ToastSound? sound)
    {
        if (_anchor() is not { } anchor)
            return;

        // First, as the player takes a moment to start: Explorer's sound starts with the slide-in. As in Explorer, only
        // the newest toast's sound plays: an older one's stops, even for a silent toast.
        if (sound is not null)
            _sound.Play(card.Toast.AppId, sound.Source, sound.Loop);
        else
            _sound.Stop();

        var window = new PanelWindow(card, activates: false);
        (ElementTheme theme, Color? accent) = _theme();
        window.SetTheme(theme, accent);

        DispatcherQueueTimer timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = UserNotifications.PopupDuration;
        timer.IsRepeating = false;
        var toast = new Toast(window, card, timer, balloonKey, completed);
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
        card.Invoked += _ => Dismiss(toast, BalloonEvent.Clicked);
        card.TurnOffRequested += _ =>
        {
            _center.TurnOff(card.Toast.AppId);
            Dismiss(toast);
        };
        card.SettingsRequested += () => Launcher.OpenSettings(_runMode, "Notifications", "ms-settings:notifications");

        _shown.Add(toast);
        _sounding = sound is null ? null : toast;
        while (_shown.Count > MaxShown)
            Dismiss(_shown[0]);

        double scale = anchor.Monitor.Dpi / 96.0;
        RectInt32 screen = anchor.Monitor.Bounds;
        int width = (int)Math.Ceiling(ToastWidth * scale);
        int height = (int)Math.Ceiling(window.MeasureHeight(ToastWidth, double.PositiveInfinity) * scale);
        window.SlideIn(Stack(new RectInt32(0, 0, width, height), toast), screen.X + screen.Width, s_slideIn);
        Restack(except: toast);
        timer.Start();
        Log.Info($"Toast from {card.Toast.AppId}");
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

    /// <param name="result">What a tray balloon's app hears; a toast that times out, is closed or is pushed out by
    /// newer ones times out, as Explorer's do.</param>
    private void Dismiss(Toast toast, BalloonEvent? result = BalloonEvent.TimedOut)
    {
        if (!_shown.Remove(toast))
            return;

        toast.Timer.Stop();
        if (_sounding == toast)
        {
            _sound.Stop();
            _sounding = null;
        }
        if (result is { } balloonEvent)
            toast.Completed?.Invoke(balloonEvent);
        RectInt32 bounds = toast.Window.ScreenBounds;
        toast.Window.SlideOut(bounds.X + bounds.Width * 2, s_slideOut, toast.Window.Close);
        Restack();
    }

    // A toast cleared from the notification center (or by its app) goes too.
    private void OnNotificationsChanged()
    {
        // Tray balloons aren't stored.
        foreach (Toast toast in _shown.Where(t => t.BalloonKey is null && !_center.IsStored(t.Card.Toast.Id)).ToList())
            Dismiss(toast);
    }
}
