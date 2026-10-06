using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Location;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Globalization.NumberFormatting;

namespace NeoShell.Widgets;

/// <summary>The weather now and over the next hours, where the computer is or at a place of the user's choosing.</summary>
internal sealed partial class WeatherWidget : WidgetView
{
    private static readonly TimeSpan s_refresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan s_retry = TimeSpan.FromMinutes(5);

    // Asked once a session, and again only when the user turns "Use my location" back on: the town doesn't move, and
    // while location is off Windows asks the user each time.
    private static Task<(double Latitude, double Longitude)?>? s_deviceLocation;

    private readonly DispatcherQueueTimer _timer;
    private CancellationTokenSource? _fetch;

    public WeatherWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public override void Close()
    {
        _timer.Stop();
        _fetch?.Cancel();
    }

    public override FrameworkElement CreateSettings()
    {
        var useDevice = new ToggleSwitch { Header = "Use my location", IsOn = Settings.Latitude is null || Settings.Longitude is null };
        var latitude = new NumberBox
        {
            Header = "Latitude",
            Minimum = -90,
            Maximum = 90,
            Value = Settings.Latitude ?? KnownDeviceLocation?.Latitude ?? double.NaN,
            IsEnabled = !useDevice.IsOn,
            NumberFormatter = CoordinateFormatter(),
        };
        var longitude = new NumberBox
        {
            Header = "Longitude",
            Minimum = -180,
            Maximum = 180,
            Value = Settings.Longitude ?? KnownDeviceLocation?.Longitude ?? double.NaN,
            IsEnabled = !useDevice.IsOn,
            NumberFormatter = CoordinateFormatter(),
        };

        void Save()
        {
            if (useDevice.IsOn)
                SaveSettings(Settings with { Latitude = null, Longitude = null });
            else if (!double.IsNaN(latitude.Value) && !double.IsNaN(longitude.Value))
                SaveSettings(Settings with { Latitude = Math.Round(latitude.Value, 4), Longitude = Math.Round(longitude.Value, 4) });
            else
                return;
            Refresh();
        }
        useDevice.Toggled += (_, _) =>
        {
            latitude.IsEnabled = longitude.IsEnabled = !useDevice.IsOn;
            if (useDevice.IsOn)
                s_deviceLocation = null;
            Save();
        };
        latitude.ValueChanged += (_, _) => Save();
        longitude.ValueChanged += (_, _) => Save();

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(useDevice);
        panel.Children.Add(latitude);
        panel.Children.Add(longitude);
        panel.Children.Add(new TextBlock
        {
            Text = "A place's coordinates are on any map: 59.91, 10.75 is Oslo.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        // MET's licence asks for credit; here rather than on the widget, to keep it small.
        panel.Children.Add(new TextBlock
        {
            Text = "Weather data from MET Norway (api.met.no).",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        return panel;
    }

    private async void Refresh()
    {
        _fetch?.Cancel();
        var fetch = _fetch = new CancellationTokenSource();
        try
        {
            (double Latitude, double Longitude)? place;
            if (Settings.Latitude is { } latitude && Settings.Longitude is { } longitude)
            {
                place = (latitude, longitude);
            }
            else
            {
                // Windows may be asking the user whether to turn location on.
                if (s_deviceLocation is null && StatusText.Visibility == Visibility.Visible)
                    StatusText.Text = "Finding your location…";
                place = await (s_deviceLocation ??= DeviceLocation.GetAsync());
            }
            if (fetch.IsCancellationRequested)
                return;
            if (place is not { } at)
            {
                ShowStatus("Your location isn't available. Turn on location for desktop apps in Windows' settings, or choose a place in this widget's settings.");
                return;
            }

            WeatherForecast? forecast = await MetWeather.GetAsync(at.Latitude, at.Longitude, fetch.Token);
            if (fetch.IsCancellationRequested)
                return;
            if (forecast is null)
            {
                ShowStatus("There's no forecast for this place.", retry: true);
                return;
            }
            Show(forecast);
        }
        catch (OperationCanceledException) when (fetch.IsCancellationRequested)
        {
            // Closed, or a newer refresh took over.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Warn("Could not get the weather", ex);
            ShowStatus("The weather couldn't be fetched. It's tried again in a few minutes.", retry: true);
        }
        finally
        {
            if (!fetch.IsCancellationRequested)
                MarkReady();
        }
    }

    protected override bool LoadsContent => true;

    private static (double Latitude, double Longitude)? KnownDeviceLocation =>
        s_deviceLocation is { IsCompletedSuccessfully: true } found ? found.Result : null;

    private void Show(WeatherForecast forecast)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        NowSymbol.Text = MetWeather.Emoji(forecast.Now.Symbol);
        NowTemperature.Text = Temperature(forecast.Now.Temperature);
        NowDescription.Text = MetWeather.Describe(forecast.Now.Symbol);
        NowWind.Text = string.Format(culture, "Wind {0:0} m/s", forecast.WindSpeed);

        Hours.Children.Clear();
        Hours.ColumnDefinitions.Clear();
        for (int i = 0; i < forecast.Hours.Count; i++)
        {
            WeatherHour hour = forecast.Hours[i];
            Hours.ColumnDefinitions.Add(new ColumnDefinition());
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(new TextBlock
            {
                Text = hour.Time.ToLocalTime().ToString("t", culture),
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            column.Children.Add(new TextBlock { Text = MetWeather.Emoji(hour.Symbol), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
            column.Children.Add(new TextBlock
            {
                Text = Temperature(hour.Temperature),
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            Grid.SetColumn(column, i);
            Hours.Children.Add(column);
        }

        Current.Visibility = Hours.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        Schedule(s_refresh);
    }

    private void ShowStatus(string text, bool retry = false)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        Current.Visibility = Hours.Visibility = Visibility.Collapsed;
        Schedule(retry ? s_retry : s_refresh);
    }

    private void Schedule(TimeSpan after)
    {
        _timer.Interval = after;
        _timer.Start();
    }

    // Four decimals, as MET takes them (about 10 metres).
    private static DecimalFormatter CoordinateFormatter() => new()
    {
        IntegerDigits = 1,
        FractionDigits = 0,
        NumberRounder = new IncrementNumberRounder { Increment = 0.0001, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
    };

    private static string Temperature(double celsius) => Math.Round(celsius).ToString(CultureInfo.CurrentCulture) + "°";
}
