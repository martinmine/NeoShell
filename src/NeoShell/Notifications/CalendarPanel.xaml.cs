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
    private int _focusMinutes = NotificationDisplay.DefaultFocusMinutes;

    internal CalendarPanel(SettingsStore settings, FocusSession focus)
    {
        InitializeComponent();
        _settings = settings;
        _focus = focus;
        _focus.Changed += ShowFocus;
    }

    /// <summary>The month was folded away or shown again; the window should be measured again.</summary>
    public event Action? ContentResized;

    /// <summary>A focus session started: Explorer's flyout closes then.</summary>
    public event Action? CloseRequested;

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
        // Explorer's calendar starts from half an hour each time it opens.
        _focusMinutes = NotificationDisplay.DefaultFocusMinutes;
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
        bool active = _focus.IsActive;
        FocusLength.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        FocusingText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        FocusMinutesRun.Text = _focusMinutes.ToString(CultureInfo.CurrentCulture) + " ";
        AutomationProperties.SetName(FocusLengthText, $"{_focusMinutes} minutes");
        LessFocusButton.IsEnabled = _focusMinutes > NotificationDisplay.MinFocusMinutes;
        MoreFocusButton.IsEnabled = _focusMinutes < NotificationDisplay.MaxFocusMinutes;
        // Explorer's filled square and triangle.
        FocusGlyph.Glyph = active ? "" : "";
        FocusButtonText.Text = active ? "End session" : "Focus";
        AutomationProperties.SetName(FocusButton, FocusButtonText.Text);
        FocusButton.IsEnabled = _focus.IsAvailable;
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Update(_settings.Current with { CalendarCollapsed = !_settings.Current.CalendarCollapsed });
        ShowCollapsed();
        ContentResized?.Invoke();
    }

    private void LessFocusButton_Click(object sender, RoutedEventArgs e) => SetFocusMinutes(NotificationDisplay.LessFocus(_focusMinutes));

    private void MoreFocusButton_Click(object sender, RoutedEventArgs e) => SetFocusMinutes(NotificationDisplay.MoreFocus(_focusMinutes));

    private void SetFocusMinutes(int minutes)
    {
        _focusMinutes = minutes;
        ShowFocus();
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focus.IsActive)
            _focus.End();
        else
        {
            _focus.Start(_focusMinutes);
            CloseRequested?.Invoke();
        }
    }
}
