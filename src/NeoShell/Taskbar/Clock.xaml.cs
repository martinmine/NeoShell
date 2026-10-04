using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Taskbar;

/// <summary>
/// Time and short date, and Do not disturb's bell while it's on; the tooltip has the full date, and a click opens
/// the notification center and calendar.
/// </summary>
public sealed partial class Clock : UserControl
{
    private readonly DispatcherQueueTimer _timer;

    public Clock()
    {
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => _timer.Stop();
    }

    public event Action? Clicked;

    /// <summary>Shows the current time and schedules the next update for just after the minute changes.</summary>
    public void Update()
    {
        DateTime now = DateTime.Now;
        TimeText.Text = now.ToString("t");
        DateText.Text = now.ToString("d");
        // As Explorer's: the full date, then the day and time again.
        ToolTipService.SetToolTip(ClockButton, $"{now:D}\n\n{now:ddd} {now:t} (Local time)");

        // One timer per minute rather than polling; re-armed each time so it never drifts.
        _timer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond - 50);
        _timer.Start();
    }

    public void ShowDoNotDisturb(bool on) => DoNotDisturbIcon.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    private void ClockButton_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke();
}
