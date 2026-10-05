using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace NeoShell.Widgets;

internal sealed partial class ResourceRow : UserControl
{
    private readonly bool _showBar;
    private IReadOnlyList<double> _history = [];
    private double _max = 100;

    public ResourceRow(string label, string automationId, bool showBar)
    {
        InitializeComponent();
        LabelText.Text = label;
        AutomationProperties.SetName(Header, label);
        AutomationProperties.SetAutomationId(Header, automationId);
        _showBar = showBar;
        ShowExpanded();
    }

    public bool IsExpanded { get; private set; }

    public void SetColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        Bar.Foreground = brush;
        Line.Stroke = brush;
        Area.Fill = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
    }

    /// <param name="percent">Fills the bar; null for a resource without one (the network).</param>
    /// <param name="max">The value at the top of the graph.</param>
    public void Show(string value, double? percent, IReadOnlyList<double> history, double max)
    {
        ValueText.Text = value;
        if (percent is { } fill)
            Bar.Value = fill;
        _history = history;
        _max = max;
        if (IsExpanded)
            Draw();
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
        ShowExpanded();
        Draw();
    }

    private void ShowExpanded()
    {
        Graph.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        // The graph shows the same, and more.
        Bar.Visibility = _showBar && !IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        Chevron.Glyph = IsExpanded ? "\uE70E" : "\uE70D";
        AutomationProperties.SetHelpText(Header, IsExpanded ? "Expanded" : "Collapsed");
    }

    private void Graph_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    // The minute fills the width, newest at the right; a shorter history starts part way across.
    private void Draw()
    {
        double width = Graph.ActualWidth;
        double height = Graph.ActualHeight;
        var line = new PointCollection();
        if (width > 0 && _history.Count > 1 && _max > 0)
        {
            int offset = ResourceMonitor.HistoryLength - _history.Count;
            for (int i = 0; i < _history.Count; i++)
            {
                double x = width * (offset + i) / (ResourceMonitor.HistoryLength - 1);
                double y = height - height * Math.Clamp(_history[i] / _max, 0, 1);
                line.Add(new Point(x, y));
            }
        }
        Line.Points = line;

        var area = new PointCollection();
        if (line.Count > 0)
        {
            foreach (Point point in line)
                area.Add(point);
            area.Add(new Point(line[^1].X, height));
            area.Add(new Point(line[0].X, height));
        }
        Area.Points = area;
    }
}
