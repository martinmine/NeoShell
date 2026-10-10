using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Performance;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// Processor, GPU, memory, disk and network use; each expands to the last minute's graph, in a colour of the user's.
/// Disks also show their free space, and disks and network adapters can be shown one by one.
/// </summary>
internal sealed partial class ResourcesWidget : WidgetView
{
    private const string CpuDefault = "#0078D4";
    private const string GpuDefault = "#00B7C3";
    private const string MemoryDefault = "#8764B8";
    private const string DiskDefault = "#107C10";
    private const string NetworkDefault = "#FF8C00";

    private readonly ResourceMonitor _monitor;
    // By ResourceMonitor's keys; kept while shown, so an open graph stays open.
    private readonly Dictionary<string, ResourceRow> _rows = [];

    /// <summary>One row as it shows now.</summary>
    /// <param name="Fill">The bar's fill in percent; null for no bar.</param>
    /// <param name="BarInGraph">The bar shows what the graph shows.</param>
    /// <param name="Max">The value at the top of the graph.</param>
    private sealed record Line(string Key, string Label, string AutomationId, string Value, double? Fill, bool BarInGraph, double Max);

    public ResourcesWidget(WidgetSettings settings, ResourceMonitor monitor)
    {
        Settings = settings;
        _monitor = monitor;
        InitializeComponent();
        _monitor.Sampled += Refresh;
        Refresh();
    }

    private bool ShowEachDrive => Settings.ShowEachDrive ?? false;

    private bool ShowEachAdapter => Settings.ShowEachAdapter ?? false;

    public override bool ContentUnderButtons => true;

    public override void Close() => _monitor.Sampled -= Refresh;

    public override FrameworkElement CreateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Toggle("Show each drive", ShowEachDrive, on => Settings with { ShowEachDrive = on }));
        panel.Children.Add(Toggle("Show each network adapter", ShowEachAdapter, on => Settings with { ShowEachAdapter = on }));
        panel.Children.Add(new TextBlock { Text = "Graph colours", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(ColorRow("CPU", Settings.CpuColor ?? CpuDefault, color => Settings with { CpuColor = color }));
        panel.Children.Add(ColorRow("GPU", Settings.GpuColor ?? GpuDefault, color => Settings with { GpuColor = color }));
        panel.Children.Add(ColorRow("Memory", Settings.MemoryColor ?? MemoryDefault, color => Settings with { MemoryColor = color }));
        panel.Children.Add(ColorRow("Disk", Settings.DiskColor ?? DiskDefault, color => Settings with { DiskColor = color }));
        panel.Children.Add(ColorRow("Network", Settings.NetworkColor ?? NetworkDefault, color => Settings with { NetworkColor = color }));
        return panel;
    }

    private void Refresh()
    {
        if (_monitor.Latest is not { } usage)
            return;

        List<Line> lines = Lines(usage);
        ShowRows(lines);
        foreach (Line line in lines)
            _rows[line.Key].Show(line.Value, line.Fill, _monitor.History(line.Key), line.Max);
    }

    private List<Line> Lines(UsageSample usage)
    {
        List<Line> lines = [new(ResourceMonitor.Cpu, "CPU", "CpuResourceRow", Percent(usage.CpuPercent), usage.CpuPercent, true, 100)];
        if (usage.GpuPercent is { } gpu)
            lines.Add(new(ResourceMonitor.Gpu, "GPU", "GpuResourceRow", Percent(gpu), gpu, true, 100));
        lines.Add(new(
            ResourceMonitor.Memory,
            "Memory",
            "MemoryResourceRow",
            $"{WidgetFormat.Gigabytes(usage.MemoryUsed)} / {WidgetFormat.Gigabytes(usage.MemoryTotal)}",
            usage.MemoryTotal == 0 ? 0 : 100.0 * usage.MemoryUsed / usage.MemoryTotal,
            true,
            100));

        // A disk's bar is its space used, as in Explorer; the graph is how busy it is.
        if (ShowEachDrive)
        {
            foreach (DriveUsage drive in _monitor.Drives)
            {
                lines.Add(new(
                    ResourceMonitor.DriveKey(drive.Name),
                    $"Disk ({drive.Name})",
                    $"DiskResourceRow_{drive.Name[0]}",
                    $"{Percent(drive.BusyPercent)} · {WidgetFormat.Size(drive.Free)} free",
                    UsedPercent(drive.Free, drive.Total),
                    false,
                    100));
            }
        }
        else
        {
            ulong free = (ulong)_monitor.Drives.Sum(drive => (double)drive.Free);
            ulong total = (ulong)_monitor.Drives.Sum(drive => (double)drive.Total);
            lines.Add(new(
                ResourceMonitor.Disk,
                "Disk",
                "DiskResourceRow",
                $"{Percent(usage.DiskPercent)} · {WidgetFormat.Size(free)} free",
                UsedPercent(free, total),
                false,
                100));
        }

        // A network's graph scales to the busiest moment of the minute.
        if (ShowEachAdapter)
        {
            for (int i = 0; i < _monitor.Adapters.Count; i++)
            {
                AdapterUsage adapter = _monitor.Adapters[i];
                string key = ResourceMonitor.AdapterKey(adapter.Id);
                lines.Add(new(key, adapter.Name, $"NetworkResourceRow_{i}", Rates(adapter.DownloadRate, adapter.UploadRate), null, false, Peak(key)));
            }
        }
        else
        {
            lines.Add(new(ResourceMonitor.Network, "Network", "NetworkResourceRow", Rates(_monitor.DownloadRate, _monitor.UploadRate), null, false, Peak(ResourceMonitor.Network)));
        }
        return lines;
    }

    // The rows change when a drive or adapter comes or goes, or one by one is turned on or off.
    private void ShowRows(List<Line> lines)
    {
        if (lines.Select(line => line.Key).SequenceEqual(_rows.Keys) && Rows.Children.Count == _rows.Count)
            return;

        Rows.Children.Clear();
        var rows = new Dictionary<string, ResourceRow>();
        foreach (Line line in lines)
        {
            if (!_rows.TryGetValue(line.Key, out ResourceRow? row))
            {
                row = new ResourceRow(line.Label, line.AutomationId, showBar: line.Fill is not null, line.BarInGraph);
                row.SetColor(WidgetFormat.ParseColor(ColorOf(line.Key), DefaultColorOf(line.Key)));
            }
            rows[line.Key] = row;
            Rows.Children.Add(row);
        }
        _rows.Clear();
        foreach ((string key, ResourceRow row) in rows)
            _rows[key] = row;
    }

    private void ShowColors()
    {
        foreach ((string key, ResourceRow row) in _rows)
            row.SetColor(WidgetFormat.ParseColor(ColorOf(key), DefaultColorOf(key)));
    }

    private string? ColorOf(string key) => Kind(key) switch
    {
        ResourceMonitor.Cpu => Settings.CpuColor,
        ResourceMonitor.Gpu => Settings.GpuColor,
        ResourceMonitor.Memory => Settings.MemoryColor,
        ResourceMonitor.Disk => Settings.DiskColor,
        _ => Settings.NetworkColor,
    };

    private static string DefaultColorOf(string key) => Kind(key) switch
    {
        ResourceMonitor.Cpu => CpuDefault,
        ResourceMonitor.Gpu => GpuDefault,
        ResourceMonitor.Memory => MemoryDefault,
        ResourceMonitor.Disk => DiskDefault,
        _ => NetworkDefault,
    };

    // Drives take the disk's colour, adapters the network's.
    private static string Kind(string key) =>
        key.StartsWith("drive:", StringComparison.Ordinal) ? ResourceMonitor.Disk
        : key.StartsWith("adapter:", StringComparison.Ordinal) ? ResourceMonitor.Network
        : key;

    private double Peak(string key) => _monitor.History(key).DefaultIfEmpty(0).Max();

    private static double UsedPercent(ulong free, ulong total) => total == 0 ? 0 : 100.0 * (total - free) / total;

    private static string Percent(double value) => Math.Round(value).ToString(CultureInfo.CurrentCulture) + "%";

    private static string Rates(double down, double up) => $"↓ {WidgetFormat.BitRate(down)}  ↑ {WidgetFormat.BitRate(up)}";

    private ToggleSwitch Toggle(string header, bool on, Func<bool, WidgetSettings> change)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = on };
        toggle.Toggled += (_, _) =>
        {
            SaveSettings(change(toggle.IsOn));
            Refresh();
        };
        return toggle;
    }

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
