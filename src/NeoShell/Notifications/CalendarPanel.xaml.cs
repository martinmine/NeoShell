using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Notifications;

public sealed partial class CalendarPanel : UserControl
{
    private readonly SettingsStore _settings;
    private readonly FocusSession _focus;

    internal CalendarPanel(SettingsStore settings, FocusSession focus)
    {
        InitializeComponent();
        _settings = settings;
        _focus = focus;
        _focus.Changed += ShowFocus;
    }

    /// <summary>The month was folded away or shown again; the window should be measured again.</summary>
    public event Action? ContentResized;

    /// <summary>Shows today, in the regional formats as they are now.</summary>
    public void Opening()
    {
        DateTime today = DateTime.Today;
        DayText.Text = NotificationDisplay.DayHeading(today, CultureInfo.CurrentCulture);
        // Windows' first day of the week (Settings → Time and language → Language and region), not the language's.
        Calendar.FirstDayOfWeek = (Windows.Globalization.DayOfWeek)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        Calendar.DisplayMode = CalendarViewDisplayMode.Month;
        Calendar.SetDisplayDate(today);
        ShowCollapsed();
        ShowFocus();
    }

    private void ShowCollapsed()
    {
        bool collapsed = _settings.Current.CalendarCollapsed;
        MonthArea.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseGlyph.Glyph = collapsed ? "" : "";
        string name = collapsed ? "Expand calendar" : "Collapse calendar";
        AutomationProperties.SetName(CollapseButton, name);
        ToolTipService.SetToolTip(CollapseButton, name);
    }

    private void ShowFocus()
    {
        bool running = _focus.EndsAt is not null;
        int minutes = _settings.Current.FocusMinutes;
        FocusLength.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        FocusRemainingText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        FocusRemainingText.Text = NotificationDisplay.RemainingText(_focus.Remaining);
        FocusMinutesRun.Text = minutes.ToString(CultureInfo.CurrentCulture) + " ";
        AutomationProperties.SetName(FocusLengthText, $"{minutes} minutes");
        LessFocusButton.IsEnabled = minutes > NotificationDisplay.MinFocusMinutes;
        MoreFocusButton.IsEnabled = minutes < NotificationDisplay.MaxFocusMinutes;
        FocusGlyph.Glyph = running ? "" : "";
        FocusButtonText.Text = running ? "Stop focus" : "Focus";
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Update(_settings.Current with { CalendarCollapsed = !_settings.Current.CalendarCollapsed });
        ShowCollapsed();
        ContentResized?.Invoke();
    }

    private void LessFocusButton_Click(object sender, RoutedEventArgs e) => SetFocusMinutes(NotificationDisplay.LessFocus(_settings.Current.FocusMinutes));

    private void MoreFocusButton_Click(object sender, RoutedEventArgs e) => SetFocusMinutes(NotificationDisplay.MoreFocus(_settings.Current.FocusMinutes));

    private void SetFocusMinutes(int minutes)
    {
        _settings.Update(_settings.Current with { FocusMinutes = minutes });
        ShowFocus();
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focus.EndsAt is null)
            _focus.Start(_settings.Current.FocusMinutes);
        else
            _focus.Stop();
    }
}
