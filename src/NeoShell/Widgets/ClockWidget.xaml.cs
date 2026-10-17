using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Settings;
using Windows.Foundation;

namespace NeoShell.Widgets;

/// <summary>An analogue clock, as Vista's gadget: of this computer or of another time zone, with a name of its own.</summary>
internal sealed partial class ClockWidget : WidgetView
{
    private readonly DispatcherQueueTimer _timer;

    public ClockWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        DrawMarks();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => ShowTime();
        ShowTime();
    }

    // Vista's clock had its second hand by default.
    private bool ShowSeconds => Settings.ShowSeconds ?? true;

    public override void Close() => _timer.Stop();

    public override FrameworkElement CreateSettings()
    {
        var name = new TextBox { Header = "Clock name", Text = Settings.ClockName ?? "", PlaceholderText = "Optional" };
        name.TextChanged += (_, _) =>
        {
            SaveSettings(Settings with { ClockName = string.IsNullOrWhiteSpace(name.Text) ? null : name.Text.Trim() });
            ShowTime();
        };

        var zones = TimeZoneInfo.GetSystemTimeZones();
        var zone = new ComboBox { Header = "Time zone", HorizontalAlignment = HorizontalAlignment.Stretch };
        zone.Items.Add("This computer's time");
        foreach (TimeZoneInfo info in zones)
            zone.Items.Add(info.DisplayName);
        int index = Settings.TimeZoneId is { } id ? zones.ToList().FindIndex(z => z.Id == id) : -1;
        zone.SelectedIndex = index + 1;
        zone.SelectionChanged += (_, _) =>
        {
            SaveSettings(Settings with { TimeZoneId = zone.SelectedIndex > 0 ? zones[zone.SelectedIndex - 1].Id : null });
            ShowTime();
        };

        var seconds = new ToggleSwitch { Header = "Show the second hand", IsOn = ShowSeconds };
        seconds.Toggled += (_, _) =>
        {
            SaveSettings(Settings with { ShowSeconds = seconds.IsOn });
            ShowTime();
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(name);
        panel.Children.Add(zone);
        panel.Children.Add(seconds);
        return panel;
    }

    // Twelve marks for the hours and finer ones for the minutes between them, inside the face's edge.
    private void DrawMarks()
    {
        var hours = new GeometryGroup();
        var minutes = new GeometryGroup();
        for (int i = 0; i < 60; i++)
        {
            double angle = i * Math.PI / 30;
            bool hour = i % 5 == 0;
            var mark = new LineGeometry { StartPoint = OnFace(angle, hour ? 58 : 63), EndPoint = OnFace(angle, 67) };
            (hour ? hours : minutes).Children.Add(mark);
        }
        HourMarks.Data = hours;
        MinuteMarks.Data = minutes;
    }

    private static Point OnFace(double angle, double radius) => new(75 + radius * Math.Sin(angle), 75 - radius * Math.Cos(angle));

    // Ticks just after each second or minute turns, as the profile widget's clock.
    private void ShowTime()
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime there = ClockFace.TimeIn(Settings.TimeZoneId, utcNow);
        (HourAngle.Angle, MinuteAngle.Angle, SecondAngle.Angle) = ClockFace.Angles(there.TimeOfDay);
        SecondHand.Visibility = ShowSeconds ? Visibility.Visible : Visibility.Collapsed;

        NameText.Text = Settings.ClockName ?? "";
        NameText.Visibility = Settings.ClockName is null ? Visibility.Collapsed : Visibility.Visible;
        // Another zone says how it differs from here; this computer's needs nothing said.
        bool elsewhere = ClockFace.Zone(Settings.TimeZoneId).Id != TimeZoneInfo.Local.Id;
        DifferenceText.Text = elsewhere ? ClockFace.Difference(there, TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.Local)) : "";
        DifferenceText.Visibility = elsewhere ? Visibility.Visible : Visibility.Collapsed;
        Caption.Visibility = NameText.Visibility == Visibility.Visible || elsewhere ? Visibility.Visible : Visibility.Collapsed;

        CultureInfo culture = CultureInfo.CurrentCulture;
        AutomationProperties.SetName(Face, there.ToString(culture.DateTimeFormat.ShortTimePattern, culture));

        DateTime now = utcNow.ToLocalTime();
        double untilNext = ShowSeconds ? 1000 - now.Millisecond : (60 - now.Second) * 1000 - now.Millisecond;
        _timer.Interval = TimeSpan.FromMilliseconds(untilNext + 20);
        _timer.Start();
    }
}
