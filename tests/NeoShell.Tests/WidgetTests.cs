using System.Globalization;
using NeoShell.Interop.Native;
using NeoShell.Interop.Performance;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Widgets;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Tests;

public sealed class WidgetTests
{
    [Theory]
    [InlineData(320, 96u, 320)]
    [InlineData(320, 144u, 480)]
    [InlineData(100, 96u, 240)]   // narrower than the minimum
    [InlineData(2000, 96u, 560)]  // wider than the maximum
    public void Sidebar_width_is_kept_in_range_and_scales_with_dpi(double width, uint dpi, int expected)
    {
        Assert.Equal(expected, SidebarLayout.PhysicalWidth(width, dpi));
    }

    [Fact]
    public void Sidebar_runs_down_the_right_of_the_monitor_to_the_taskbar()
    {
        var monitor = new RectInt32(0, 0, 1920, 1080);
        // While NeoShell is the shell, the work area already leaves the sidebar out.
        var workArea = new RectInt32(0, 0, 1600, 1032);

        Assert.Equal(new RectInt32(1600, 0, 320, 1032), SidebarLayout.Bounds(monitor, workArea, 320));
    }

    [Fact]
    public void Alongside_explorer_the_sidebar_docks_left_of_app_bars_on_the_right()
    {
        // Another app bar takes the rightmost 100 pixels; the shell answered ABM_QUERYPOS with a rect ending there.
        var queried = new User32.RECT { left = 0, top = 0, right = 1820, bottom = 1032 };

        User32.RECT docked = AppBar.AlignToRight(queried, 320);

        Assert.Equal(new RectInt32(1500, 0, 320, 1032), docked.ToRectInt32());
    }

    [Fact]
    public void Floating_widget_stays_where_it_was_left_while_on_a_screen()
    {
        RectInt32[] workAreas = [new(0, 0, 1920, 1032), new(-1280, 0, 1280, 1024)];

        Assert.Equal(new PointInt32(-900, 300),
            SidebarLayout.KeepOnScreen(new PointInt32(-900, 300), 300, workAreas, workAreas[0], 24));
    }

    [Fact]
    public void Floating_widget_on_a_monitor_that_is_gone_comes_back_to_the_top_right_of_the_primary()
    {
        RectInt32[] workAreas = [new(0, 0, 1920, 1032)];

        Assert.Equal(new PointInt32(1596, 24),
            SidebarLayout.KeepOnScreen(new PointInt32(-900, 300), 300, workAreas, workAreas[0], 24));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(150, 1)]
    [InlineData(500, 3)]
    public void Dropped_widget_goes_before_the_first_card_whose_middle_is_below_the_pointer(double y, int expected)
    {
        Assert.Equal(expected, SidebarLayout.DropIndex([50, 200, 350], y));
    }

    [Fact]
    public void Docking_puts_the_widget_among_the_docked_ones_and_forgets_its_position()
    {
        IReadOnlyList<WidgetSettings> widgets =
        [
            new() { Id = "a" },
            new() { Id = "floating", X = 10, Y = 10 },
            new() { Id = "b" },
            new() { Id = "c" },
            new() { Id = "moved", X = 500, Y = 600 },
        ];

        IReadOnlyList<WidgetSettings> docked = SidebarLayout.Dock(widgets, "moved", 1);

        Assert.Equal(["a", "floating", "moved", "b", "c"], docked.Select(w => w.Id));
        Assert.False(docked[2].IsFloating);
    }

    [Fact]
    public void Docking_past_the_last_card_adds_at_the_end()
    {
        IReadOnlyList<WidgetSettings> widgets = [new() { Id = "a" }, new() { Id = "b" }];

        Assert.Equal(["b", "a"], SidebarLayout.Dock(widgets, "a", 5).Select(w => w.Id));
    }

    [Theory]
    [InlineData(0, "0 Kbps")]
    [InlineData(105_000, "840 Kbps")]
    [InlineData(1_550_000, "12.4 Mbps")]
    [InlineData(250_000_000, "2.0 Gbps")]
    public void Network_speed_is_written_in_bits_a_second(double bytesPerSecond, string expected)
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(expected, WidgetFormat.BitRate(bytesPerSecond));
    }

    [Theory]
    [InlineData(40_802_189_312UL, "38.0 GB")]
    [InlineData(549_755_813_888UL, "512 GB")]
    [InlineData(1_979_120_929_996UL, "1.8 TB")]
    public void Free_space_is_written_as_explorer_does(ulong bytes, string expected)
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(expected, WidgetFormat.Size(bytes));
    }

    [Fact]
    public void Gpu_use_is_the_busiest_engine_summed_over_processes()
    {
        (string, double)[] engines =
        [
            ("pid_100_luid_0x00000000_0x0000D1A0_phys_0_eng_0_engtype_3D", 20),
            ("pid_200_luid_0x00000000_0x0000D1A0_phys_0_eng_0_engtype_3D", 15),
            ("pid_100_luid_0x00000000_0x0000D1A0_phys_0_eng_3_engtype_VideoDecode", 30),
            ("pid_300_luid_0x00000000_0x0000D1A0_phys_0_eng_5_engtype_Copy", 2),
        ];

        Assert.Equal(35, SystemUsage.GpuPercent(engines));
    }

    [Fact]
    public void Gpu_use_is_unknown_without_gpu_counters()
    {
        Assert.Null(SystemUsage.GpuPercent([]));
    }

    [Fact]
    public void Memory_is_written_in_gigabytes()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("5.5 GB", WidgetFormat.Gigabytes(5_905_580_032));
    }

    [Theory]
    [InlineData("#D13438", 0xD1, 0x34, 0x38)]
    [InlineData("#d13438", 0xD1, 0x34, 0x38)]
    [InlineData("red", 0x00, 0x78, 0xD4)]
    [InlineData(null, 0x00, 0x78, 0xD4)]
    [InlineData("#12345", 0x00, 0x78, 0xD4)]
    public void Graph_colour_falls_back_when_it_is_not_rgb(string? text, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromArgb(255, r, g, b), WidgetFormat.ParseColor(text, "#0078D4"));
    }

    private const string Forecast = """
        {
          "type": "Feature",
          "properties": {
            "timeseries": [
              { "time": "2026-10-05T16:00:00Z", "data": { "instant": { "details": { "air_temperature": 9.0, "wind_speed": 1.0 } },
                "next_1_hours": { "summary": { "symbol_code": "rain" } } } },
              { "time": "2026-10-05T17:00:00Z", "data": { "instant": { "details": { "air_temperature": 11.6, "wind_speed": 3.4 } },
                "next_1_hours": { "summary": { "symbol_code": "partlycloudy_day" } } } },
              { "time": "2026-10-05T18:00:00Z", "data": { "instant": { "details": { "air_temperature": 10.2, "wind_speed": 3.0 } },
                "next_1_hours": { "summary": { "symbol_code": "lightrainshowers_night" } } } },
              { "time": "2026-10-05T19:00:00Z", "data": { "instant": { "details": { "air_temperature": 9.4, "wind_speed": 2.0 } },
                "next_6_hours": { "summary": { "symbol_code": "cloudy" } } } }
            ]
          }
        }
        """;

    [Fact]
    public void Forecast_starts_with_the_hour_under_way()
    {
        WeatherForecast? forecast = MetWeather.Parse(Forecast, new DateTimeOffset(2026, 10, 5, 17, 40, 0, TimeSpan.Zero));

        Assert.NotNull(forecast);
        Assert.Equal(new WeatherHour(new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero), 11.6, "partlycloudy_day"), forecast.Now);
        Assert.Equal(3.4, forecast.WindSpeed);
        Assert.Equal([10.2, 9.4], forecast.Hours.Select(h => h.Temperature));
        // Further out only a six-hour symbol is given.
        Assert.Equal("cloudy", forecast.Hours[1].Symbol);
    }

    [Fact]
    public void Forecast_that_has_run_out_gives_nothing()
    {
        Assert.Null(MetWeather.Parse(Forecast, new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(MetWeather.Parse("""{ "type": "Feature" }""", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("clearsky_day", "Clear sky")]
    [InlineData("partlycloudy_night", "Partly cloudy")]
    [InlineData("lightrainshowersandthunder_day", "Light rain showers and thunder")]
    [InlineData("lightssleetshowersandthunder_day", "Light sleet showers and thunder")]
    [InlineData("heavysnow", "Heavy snow")]
    [InlineData("somethingnew", "Somethingnew")]
    [InlineData(null, "")]
    public void Weather_symbol_is_described_in_words(string? symbol, string expected)
    {
        Assert.Equal(expected, MetWeather.Describe(symbol));
    }

    [Theory]
    [InlineData("clearsky_day", "☀")]
    [InlineData("clearsky_night", "\U0001F319")]
    [InlineData("partlycloudy_day", "⛅")]
    [InlineData("rainshowers_day", "\U0001F326")]
    [InlineData("rainshowers_night", "\U0001F327")]
    [InlineData("heavyrainandthunder", "⛈")]
    [InlineData("lightsleet", "\U0001F328")]
    [InlineData("fog", "\U0001F32B")]
    public void Weather_symbol_has_a_picture(string symbol, string expected)
    {
        Assert.Equal(expected, MetWeather.Emoji(symbol));
    }

    // Sets the thread's culture for one test.
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
