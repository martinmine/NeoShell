using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Windowing;
using Windows.Foundation;
using Windows.UI;

namespace NeoShell.Themes.Concept2088;

/// <summary>
/// The 2088 concept's desktop, the Field (see docs/design/theme-2088.md): laid over the wallpaper and under the
/// desktop icons on every monitor. The wallpaper shows faintly through a dark shade, a red glow and a perspective grid
/// run to the horizon, scan lines and a slow sweep pass over it, registration marks hold the corners, and the time and
/// the machine's state are printed at the top right.
/// </summary>
internal sealed partial class FieldDesktop : Grid
{
    private readonly bool _primary;
    private readonly Canvas _floor = new() { IsHitTestVisible = false };
    private readonly Canvas _marks = new() { IsHitTestVisible = false };
    private readonly Rectangle _sweep = new() { Height = 120, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly TranslateTransform _sweepAt = new();
    private readonly Storyboard _sweeping = new();
    private readonly TextBlock _time = Hud.Label("", 128, Hud.Text);
    private readonly TextBlock _seconds = Hud.Label("", 28, Hud.Red, Hud.Mono);
    private readonly TextBlock _date = Hud.Label("", 15, Hud.Dim, Hud.Mono, spacing: 200);
    private readonly TextBlock _uptime = Hud.Label("", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _cpu = Hud.Label("--", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _memory = Hud.Label("--", 11, Hud.Text, Hud.Mono);
    private readonly SegmentBar _cpuBar = new(24, 6, 8);
    private readonly SegmentBar _memoryBar = new(24, 6, 8);
    private readonly DispatcherQueueTimer _clock;

    public FieldDesktop(DisplayMonitor monitor, int number)
    {
        _primary = monitor.IsPrimary;
        IsHitTestVisible = false;
        Children.Add(new Rectangle { Fill = Hud.Solid(Hud.Void, 0.84) });
        Children.Add(new Rectangle { Fill = Glow() });
        Children.Add(_floor);
        Children.Add(Watermark());
        Children.Add(new Rectangle { Fill = Hud.ScanLines(0.35) });
        _sweep.Fill = SweepBrush();
        _sweep.RenderTransform = _sweepAt;
        Children.Add(_sweep);
        Children.Add(_marks);
        Children.Add(Readouts(monitor, number));
        Children.Add(Status());
        SizeChanged += (_, e) => Draw(e.NewSize);

        _time.FontWeight = Microsoft.UI.Text.FontWeights.Light;
        _clock = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            Tick();
            _clock.Start();
            _sweeping.Begin();
            Hud.Resources.Sampled += UpdateTelemetry;
        };
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Stops the clock, the sweep and the telemetry, when the theme changes away or the window closes.</summary>
    public void Stop()
    {
        _clock.Stop();
        _sweeping.Stop();
        Hud.Resources.Sampled -= UpdateTelemetry;
    }

    private static Brush Glow() => new RadialGradientBrush
    {
        Center = new Point(0.18, 1.05),
        GradientOrigin = new Point(0.18, 1.05),
        RadiusX = 0.9,
        RadiusY = 0.75,
        GradientStops =
        {
            new GradientStop { Color = Color.FromArgb(0x60, 0xFF, 0x2A, 0x3D), Offset = 0 },
            new GradientStop { Color = Color.FromArgb(0x18, 0xFF, 0x2A, 0x3D), Offset = 0.45 },
            new GradientStop { Color = Color.FromArgb(0, 0xFF, 0x2A, 0x3D), Offset = 1 },
        },
    };

    // A band that brightens to a hairline at its bottom: the scanner passing down the screen.
    private static Brush SweepBrush() => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop { Color = Color.FromArgb(0, 0xFF, 0x2A, 0x3D), Offset = 0 },
            new GradientStop { Color = Color.FromArgb(0x14, 0xFF, 0x2A, 0x3D), Offset = 0.97 },
            new GradientStop { Color = Color.FromArgb(0x70, 0xFF, 0x2A, 0x3D), Offset = 1 },
        },
    };

    private static TextBlock Watermark()
    {
        TextBlock year = Hud.Label("2088", 420, Hud.Text, bold: true);
        year.Opacity = 0.035;
        year.CharacterSpacing = -40;
        year.HorizontalAlignment = HorizontalAlignment.Center;
        year.VerticalAlignment = VerticalAlignment.Bottom;
        year.Margin = new Thickness(0, 0, 0, 40);
        return year;
    }

    private StackPanel Readouts(DisplayMonitor monitor, int number)
    {
        // In the sky's middle: the desktop icons keep to the left and the widget sidebar to the right.
        var block = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 72, 0, 0), Spacing = 6 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        brand.Children.Add(Hud.Label("ネオシェル", 13, Hud.Red, Hud.Japanese));
        brand.Children.Add(Hud.Label("NEOSHELL OS · FIELD", 11, Hud.Red, Hud.Mono, spacing: 300));
        block.Children.Add(brand);
        if (!_primary)
        {
            // The time and the machine's state are the primary display's; the others carry only their number.
            block.Children.Add(Hud.Label($"AUX DISPLAY {number:00} · {monitor.Bounds.Width}×{monitor.Bounds.Height}", 11, Hud.Dim, Hud.Mono, spacing: 150));
            return block;
        }

        var time = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(34, 6, 0, 0) };
        time.Children.Add(_time);
        _seconds.VerticalAlignment = VerticalAlignment.Bottom;
        _seconds.Margin = new Thickness(6, 0, 0, 6);
        time.Children.Add(_seconds);
        block.Children.Add(time);
        _date.HorizontalAlignment = HorizontalAlignment.Center;
        block.Children.Add(_date);
        // "New Tokyo, District 7": where the story's machine stands.
        TextBlock place = Hud.Label("新東京 · 第七区 · SEC-07 · N 35.6762° E 139.6503°", 11, Hud.Red, Hud.Japanese, spacing: 120);
        place.HorizontalAlignment = HorizontalAlignment.Center;
        place.Margin = new Thickness(0, 4, 0, 0);
        block.Children.Add(place);

        var table = new StackPanel { Width = 320, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        table.Children.Add(new Rectangle { Height = 1, Fill = Hud.Solid(Hud.LineBright) });
        table.Children.Add(Row("OPERATOR", "操作者", Hud.Label(UserAccount.DisplayName.ToUpperInvariant(), 11, Hud.Text, Hud.Mono)));
        table.Children.Add(Row("NODE", "端末", Hud.Label(Environment.MachineName.ToUpperInvariant(), 11, Hud.Text, Hud.Mono)));
        table.Children.Add(Row("UPTIME", "稼働", _uptime));
        table.Children.Add(Row("CPU", "演算", Meter(_cpuBar, _cpu)));
        table.Children.Add(Row("MEM", "記憶", Meter(_memoryBar, _memory)));
        table.Children.Add(new Rectangle { Height = 1, Fill = Hud.Solid(Hud.LineBright) });
        block.Children.Add(table);
        return block;
    }

    private static Grid Row(string name, string japanese, FrameworkElement value)
    {
        var row = new Grid();
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        caption.Children.Add(Hud.Label(name, 10, Hud.Dim, Hud.Mono, spacing: 150));
        caption.Children.Add(Hud.Label(japanese, 10, Hud.Red, Hud.Japanese));
        row.Children.Add(caption);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(value);
        return row;
    }

    private static StackPanel Meter(SegmentBar bar, TextBlock value)
    {
        var meter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        meter.Children.Add(bar);
        value.Width = 36;
        value.TextAlignment = TextAlignment.Right;
        meter.Children.Add(value);
        return meter;
    }

    private static StackPanel Status()
    {
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, SpineTaskbarWindow.BarHeight + 22) };
        status.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Hud.Solid(Hud.Red) });
        status.Children.Add(Hud.Label("SYSTEM NOMINAL", 10, Hud.Text, Hud.Mono, spacing: 250));
        status.Children.Add(Hud.Label("正常稼働", 10, Hud.Red, Hud.Japanese));
        return status;
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        _time.Text = now.ToString("HH:mm");
        _seconds.Text = now.ToString("ss");
        _date.Text = $"{Clock2088.Date(now)} · {Clock2088.Weekday(now)}曜日";
        _uptime.Text = Clock2088.Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private void UpdateTelemetry()
    {
        if (Hud.Resources.Latest is not { } sample)
            return;
        _cpuBar.SetValue(sample.CpuPercent / 100);
        _cpu.Text = $"{sample.CpuPercent:0}%";
        double memory = sample.MemoryTotal == 0 ? 0 : (double)sample.MemoryUsed / sample.MemoryTotal;
        _memoryBar.SetValue(memory);
        _memory.Text = $"{memory * 100:0}%";
    }

    private void Draw(Size size)
    {
        // The marks keep above the Spine, which covers the bottom of the screen.
        double w = size.Width, h = size.Height - SpineTaskbarWindow.BarHeight;
        if (w <= 0 || h <= 0)
            return;

        // The floor: lines towards a vanishing point on the horizon, crossed by lines that close up into the distance.
        _floor.Children.Clear();
        double horizon = h * 0.64;
        var vanishing = new Point(w * 0.5, horizon);
        _floor.Children.Add(LineOf(0, horizon, w, horizon, Hud.Red, 0.45));
        for (int i = -14; i <= 14; i++)
            _floor.Children.Add(LineOf(vanishing.X, vanishing.Y, vanishing.X + i * w * 0.11, h, Hud.Red, 0.14));
        for (int i = 1; i <= 12; i++)
        {
            double t = i / 12.0;
            double y = horizon + (h - horizon) * t * t;
            _floor.Children.Add(LineOf(0, y, w, y, Hud.Red, 0.08 + 0.12 * t));
        }

        // The sky: faint crosses on a 120 grid, then registration marks in the corners with their coordinates.
        _marks.Children.Clear();
        for (double x = 120; x < w; x += 120)
            for (double y = 120; y < horizon - 40; y += 120)
            {
                _marks.Children.Add(LineOf(x - 4, y, x + 5, y, Hud.Dim, 0.25));
                _marks.Children.Add(LineOf(x, y - 4, x, y + 5, Hud.Dim, 0.25));
            }
        const double inset = 24, arm = 34;
        foreach ((double x, double y, int dx, int dy) in new[] { (inset, inset, 1, 1), (w - inset, inset, -1, 1), (inset, h - inset, 1, -1), (w - inset, h - inset, -1, -1) })
        {
            _marks.Children.Add(Bar(x, y, dx * arm, dy * 2));
            _marks.Children.Add(Bar(x, y, dx * 2, dy * arm));
        }
        Caption($"FIELD {w:0}×{size.Height:0}", w - inset - 12, inset + 10, true);
        Caption("REC ● 新東京", inset + 12, h - inset - 22, false);
        Caption("NS-2088 // ΔK7", w - inset - 12, h - inset - 22, true);

        // The sweep runs from above the screen to below it, every 9 seconds.
        _sweeping.Stop();
        _sweeping.Children.Clear();
        var down = new DoubleAnimation { From = -_sweep.Height, To = size.Height, Duration = TimeSpan.FromSeconds(9), RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(down, _sweepAt);
        Storyboard.SetTargetProperty(down, "Y");
        _sweeping.Children.Add(down);
        if (IsLoaded)
            _sweeping.Begin();
    }

    private void Caption(string text, double x, double y, bool fromRight)
    {
        TextBlock caption = Hud.Label(text, 9, Hud.Dim, Hud.Mono, spacing: 150);
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, fromRight ? x - caption.DesiredSize.Width : x);
        Canvas.SetTop(caption, y);
        _marks.Children.Add(caption);
    }

    private static Line LineOf(double x1, double y1, double x2, double y2, Color color, double opacity) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = Hud.Solid(color, opacity),
        StrokeThickness = 1,
    };

    // A bar from a corner, its width or height negative to run left or up.
    private static Rectangle Bar(double x, double y, double width, double height)
    {
        var bar = new Rectangle { Width = Math.Abs(width), Height = Math.Abs(height), Fill = Hud.Solid(Hud.Text, 0.85) };
        Canvas.SetLeft(bar, width < 0 ? x + width : x);
        Canvas.SetTop(bar, height < 0 ? y + height : y);
        return bar;
    }
}
