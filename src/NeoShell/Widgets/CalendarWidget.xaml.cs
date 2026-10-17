using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// A month, as Vista's calendar gadget, with Windows' first day of the week; clicking a day shows it large, a page of
/// a desk calendar, and the arrows then go from day to day.
/// </summary>
internal sealed partial class CalendarWidget : WidgetView
{
    private readonly DispatcherQueueTimer _midnight;
    private readonly Button[] _days = new Button[CalendarMonth.Weeks * 7];
    private DateOnly _today = Today;
    // The day the page shows, or a day of the month shown.
    private DateOnly _shown;
    private bool _dayPage;

    public CalendarWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        _shown = _today;
        BuildMonth();
        _midnight = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _midnight.Tick += (_, _) => NewDay();
        Show();
    }

    public override bool ContentUnderButtons => true;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static double DayOpacity(bool inMonth) => inMonth ? 1 : 0.45;

    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public override void Close() => _midnight.Stop();

    private void BuildMonth()
    {
        for (int column = 0; column < 7; column++)
            Month.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row <= CalendarMonth.Weeks; row++)
            Month.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] names = CalendarMonth.WeekdayNames(CultureInfo.CurrentCulture.DateTimeFormat);
        for (int column = 0; column < 7; column++)
        {
            var name = new TextBlock { Text = names[column], Style = (Style)Resources["WeekdayTextStyle"], Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetColumn(name, column);
            Month.Children.Add(name);
        }

        for (int i = 0; i < _days.Length; i++)
        {
            var day = new Button { Style = (Style)Resources["DayButtonStyle"], ContentTemplate = (DataTemplate)Resources["DayTemplate"] };
            AutomationProperties.SetAutomationId(day, "CalendarDay");
            day.Click += (sender, _) =>
            {
                _shown = ((CalendarDay)((Button)sender).Content).Date;
                _dayPage = true;
                Show();
            };
            Grid.SetRow(day, 1 + i / 7);
            Grid.SetColumn(day, i % 7);
            Month.Children.Add(day);
            _days[i] = day;
        }
    }

    private void Show()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        TitleText.Text = _shown.ToString("Y", culture);
        // The page takes the month's place at its height, so the widgets below stay where they are.
        if (_dayPage && Month.ActualHeight > 0)
            DayPage.Height = Month.ActualHeight;
        Month.Visibility = _dayPage ? Visibility.Collapsed : Visibility.Visible;
        DayPage.Visibility = _dayPage ? Visibility.Visible : Visibility.Collapsed;
        SetArrowNames(_dayPage ? "day" : "month");

        if (_dayPage)
        {
            DayNumberText.Text = _shown.Day.ToString(culture);
            WeekdayText.Text = culture.DateTimeFormat.GetDayName(_shown.DayOfWeek);
            TodayText.Visibility = _shown == _today ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(DayPage, _shown.ToString("D", culture));
        }
        else
        {
            CalendarDay[] days = CalendarMonth.Days(_shown, culture.DateTimeFormat.FirstDayOfWeek, _today);
            for (int i = 0; i < days.Length; i++)
            {
                _days[i].Content = days[i];
                AutomationProperties.SetName(_days[i], days[i].Date.ToString("D", culture));
            }
        }

        // Just after midnight, for the new day.
        _midnight.Interval = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(1);
        _midnight.Start();
    }

    private void SetArrowNames(string step)
    {
        foreach ((Button button, string name) in new[] { (PreviousButton, $"Previous {step}"), (NextButton, $"Next {step}") })
        {
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
        }
    }

    // A calendar left on today follows it to the next day (and month); one turned to another stays there.
    private void NewDay()
    {
        DateOnly yesterday = _today;
        _today = Today;
        if (_dayPage ? _shown == yesterday : _shown.Month == yesterday.Month && _shown.Year == yesterday.Year)
            _shown = _today;
        Show();
    }

    private void Step(int direction)
    {
        _shown = _dayPage ? _shown.AddDays(direction) : _shown.AddMonths(direction);
        Show();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => Step(1);

    private void TitleButton_Click(object sender, RoutedEventArgs e)
    {
        _shown = _today;
        _dayPage = false;
        Show();
    }

    private void DayPage_Click(object sender, RoutedEventArgs e)
    {
        _dayPage = false;
        Show();
    }
}
