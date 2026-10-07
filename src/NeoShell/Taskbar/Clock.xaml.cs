using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;

namespace NeoShell.Taskbar;

/// <summary>
/// Time and short date, as the user's settings have them (seconds, or hidden), then the notification bell; the
/// tooltips have the full date and the count of new notifications, and a click opens the notification center and
/// calendar.
/// </summary>
public sealed partial class Clock : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    // Kept and refilled, so a tooltip that's open follows the seconds rather than closing.
    private readonly ToolTip _timeToolTip = new();
    private readonly ToolTip _bellToolTip = new();
    private ClockSettings _settings = ClockSettings.Read();
    private bool _doNotDisturb;
    private int _newNotifications;

    public Clock()
    {
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => _timer.Stop();
        ToolTipService.SetToolTip(TimeAndDate, _timeToolTip);
        ToolTipService.SetToolTip(BellArea, _bellToolTip);
    }

    public event Action? Clicked;

    public void Apply(ClockSettings settings)
    {
        _settings = settings;
        Update();
    }

    /// <summary>Shows the current time and schedules the next update for just after the second or minute changes.</summary>
    public void Update()
    {
        _timer.Stop();
        TimeAndDate.Visibility = _settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ShowBell();
        if (!_settings.ShowClock)
            return;

        DateTime now = DateTime.Now;
        string time = RegionalTime.Format(now, _settings.ShowSeconds);
        TimeText.Text = ClockDisplay.Time(time, RegionalTime.Scripts());
        DateText.Text = now.ToString("d", CultureInfo.CurrentCulture);
        AutomationProperties.SetName(ClockButton, $"Clock {time}\n{DateText.Text}");
        _timeToolTip.Content = ClockDisplay.ToolTip(now.ToString("D", CultureInfo.CurrentCulture),
            [(now.ToString("ddd", CultureInfo.CurrentCulture), time, "Local time"), .. _settings.AdditionalClocks.Select(clock =>
            {
                DateTime there = TimeZoneInfo.ConvertTime(now, clock.Zone);
                return (there.ToString("ddd", CultureInfo.CurrentCulture), RegionalTime.Format(there, _settings.ShowSeconds), clock.Name);
            })]);

        // One timer per second or minute rather than polling; re-armed each time so it never drifts. Explorer's is
        // the same, its minute one a little late.
        _timer.Interval = _settings.ShowSeconds
            ? TimeSpan.FromMilliseconds(1001 - now.Millisecond)
            : TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond - 50);
        _timer.Start();
    }

    /// <summary>Do not disturb, and how many notifications came since the notification center was last open.</summary>
    public void ShowNotifications(bool doNotDisturb, int newNotifications)
    {
        _doNotDisturb = doNotDisturb;
        _newNotifications = newNotifications;
        ShowBell();
    }

    // Nothing at all when both are off, as Explorer's.
    private void ShowBell()
    {
        BellLook? look = ClockDisplay.Bell(_settings.ShowBell, _doNotDisturb, _newNotifications);
        BellArea.Visibility = look is null ? Visibility.Collapsed : Visibility.Visible;
        Visibility = look is null && !_settings.ShowClock ? Visibility.Collapsed : Visibility.Visible;
        if (look is null)
            return;

        Bell.Glyph = look.Glyph;
        VisualStateManager.GoToState(this, look.Accent ? "AccentBell" : "PlainBell", false);
        _bellToolTip.Content = look.ToolTip;
        AutomationProperties.SetName(Bell, $"Notifications {look.ToolTip}");
    }

    private void ClockButton_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke();
}
