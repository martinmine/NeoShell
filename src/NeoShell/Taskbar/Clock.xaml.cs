using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Taskbar;

/// <summary>Time and short date; the tooltip has the full date and a click opens a calendar.</summary>
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

    /// <summary>Shows the current time and schedules the next update for just after the minute changes.</summary>
    public void Update()
    {
        DateTime now = DateTime.Now;
        TimeText.Text = now.ToString("t");
        DateText.Text = now.ToString("d");
        ToolTipService.SetToolTip(ClockButton, now.ToString("D"));

        // One timer per minute rather than polling; re-armed each time so it never drifts.
        _timer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond - 50);
        _timer.Start();
    }

    private void CalendarFlyout_Opening(object sender, object e) => Calendar.SetDisplayDate(DateTimeOffset.Now);
}
