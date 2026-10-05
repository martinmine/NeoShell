using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Performance;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>Processor, memory, disk and network use; each expands to the last minute's graph, in a colour of the user's.</summary>
internal sealed partial class ResourcesWidget : WidgetView
{
    private const string CpuDefault = "#0078D4";
    private const string MemoryDefault = "#8764B8";
    private const string DiskDefault = "#107C10";
    private const string NetworkDefault = "#FF8C00";

    private readonly ResourceMonitor _monitor;
    private readonly ResourceRow _cpu = new("CPU", "CpuResourceRow", showBar: true);
    private readonly ResourceRow _memory = new("Memory", "MemoryResourceRow", showBar: true);
    private readonly ResourceRow _disk = new("Disk", "DiskResourceRow", showBar: true);
    private readonly ResourceRow _network = new("Network", "NetworkResourceRow", showBar: false);

    public ResourcesWidget(WidgetSettings settings, ResourceMonitor monitor)
    {
        Settings = settings;
        _monitor = monitor;
        InitializeComponent();
        foreach (ResourceRow row in (ResourceRow[])[_cpu, _memory, _disk, _network])
            Rows.Children.Add(row);
        ShowColors();
        _monitor.Sampled += Refresh;
        Refresh();
    }

    public override void Close() => _monitor.Sampled -= Refresh;

    public override FrameworkElement CreateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Graph colours", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(ColorRow("CPU", Settings.CpuColor ?? CpuDefault, color => Settings with { CpuColor = color }));
        panel.Children.Add(ColorRow("Memory", Settings.MemoryColor ?? MemoryDefault, color => Settings with { MemoryColor = color }));
        panel.Children.Add(ColorRow("Disk", Settings.DiskColor ?? DiskDefault, color => Settings with { DiskColor = color }));
        panel.Children.Add(ColorRow("Network", Settings.NetworkColor ?? NetworkDefault, color => Settings with { NetworkColor = color }));
        return panel;
    }

    private void ShowColors()
    {
        _cpu.SetColor(WidgetFormat.ParseColor(Settings.CpuColor, CpuDefault));
        _memory.SetColor(WidgetFormat.ParseColor(Settings.MemoryColor, MemoryDefault));
        _disk.SetColor(WidgetFormat.ParseColor(Settings.DiskColor, DiskDefault));
        _network.SetColor(WidgetFormat.ParseColor(Settings.NetworkColor, NetworkDefault));
    }

    private void Refresh()
    {
        if (_monitor.CpuHistory.Count == 0)
            return;

        UsageSample usage = _monitor.Latest;
        _cpu.Show(Percent(usage.CpuPercent), usage.CpuPercent, _monitor.CpuHistory, 100);
        double memoryPercent = _monitor.MemoryHistory[^1];
        _memory.Show(
            $"{WidgetFormat.Gigabytes(usage.MemoryUsed)} / {WidgetFormat.Gigabytes(usage.MemoryTotal)}",
            memoryPercent,
            _monitor.MemoryHistory,
            100);
        _disk.Show(Percent(usage.DiskPercent), usage.DiskPercent, _monitor.DiskHistory, 100);
        // The network's graph scales to the busiest moment of the minute.
        _network.Show(
            $"↓ {WidgetFormat.BitRate(_monitor.DownloadRate)}  ↑ {WidgetFormat.BitRate(_monitor.UploadRate)}",
            null,
            _monitor.NetworkHistory,
            _monitor.NetworkHistory.DefaultIfEmpty(0).Max());
    }

    private static string Percent(double value) => Math.Round(value).ToString(CultureInfo.CurrentCulture) + "%";

    // A swatch for each colour of the palette; the chosen one carries a check mark.
    private StackPanel ColorRow(string label, string chosen, Func<string, WidgetSettings> choose)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var checks = new List<FontIcon>();
        foreach (string color in WidgetFormat.Palette)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 12,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                Visibility = string.Equals(color, chosen, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed,
            };
            checks.Add(check);
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Content = new Grid
                {
                    Children =
                    {
                        new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(WidgetFormat.ParseColor(color, color)) },
                        check,
                    },
                },
            };
            AutomationProperties.SetName(swatch, $"{label} {color}");
            swatch.Click += (_, _) =>
            {
                SaveSettings(choose(color));
                ShowColors();
                foreach (FontIcon other in checks)
                    other.Visibility = other == check ? Visibility.Visible : Visibility.Collapsed;
            };
            swatches.Children.Add(swatch);
        }

        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label });
        row.Children.Add(swatches);
        return row;
    }
}
