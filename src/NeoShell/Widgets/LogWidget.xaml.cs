using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// NeoShell's event feed as it happens (<see cref="EventFeed"/>): what it logs, and app windows opening, closing and
/// coming to the front. The last few lines, newest at the bottom.
/// </summary>
internal sealed partial class LogWidget : WidgetView
{
    private readonly EventFeed _feed;

    public LogWidget(WidgetSettings settings, EventFeed feed)
    {
        Settings = settings;
        _feed = feed;
        InitializeComponent();
        // The lines from before, without sliding each one in.
        var transitions = Lines.ChildrenTransitions;
        Lines.ChildrenTransitions = null;
        foreach (FeedEntry entry in _feed.Entries.Where(Shows).TakeLast(LineCount))
            Lines.Children.Add(CreateLine(entry));
        Lines.ChildrenTransitions = transitions;
        _feed.Added += OnAdded;
    }

    private int LineCount => Math.Clamp(Settings.LogLines ?? 12, 3, 40);

    private bool ShowsWindowEvents => Settings.ShowWindowEvents ?? true;

    public override void Close() => _feed.Added -= OnAdded;

    public override FrameworkElement CreateSettings()
    {
        var lines = new NumberBox
        {
            Header = "Lines",
            Minimum = 3,
            Maximum = 40,
            Value = LineCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        lines.ValueChanged += (_, _) =>
        {
            if (!double.IsNaN(lines.Value))
                Change(Settings with { LogLines = (int)lines.Value });
        };
        var windows = new ToggleSwitch { Header = "App windows opening and closing", IsOn = ShowsWindowEvents };
        windows.Toggled += (_, _) => Change(Settings with { ShowWindowEvents = windows.IsOn });

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(lines);
        panel.Children.Add(windows);
        return panel;
    }

    private void Change(WidgetSettings settings)
    {
        SaveSettings(settings);
        Lines.Children.Clear();
        foreach (FeedEntry entry in _feed.Entries.Where(Shows).TakeLast(LineCount))
            Lines.Children.Add(CreateLine(entry));
    }

    private bool Shows(FeedEntry entry) => ShowsWindowEvents || entry.Kind is not ("OPEN" or "CLOSE" or "FOCUS");

    private void OnAdded(FeedEntry entry)
    {
        if (!Shows(entry))
            return;
        while (Lines.Children.Count >= LineCount)
            Lines.Children.RemoveAt(0);
        Lines.Children.Add(CreateLine(entry));
    }

    // Time, kind and text, one line high.
    private Grid CreateLine(FeedEntry entry)
    {
        var line = new Grid { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        ToolTipService.SetToolTip(line, entry.Text);
        line.Children.Add(Part(entry.Time.ToString("HH:mm:ss"), "LogTimeStyle", 0));
        line.Children.Add(Part(entry.Kind, KindStyle(entry.Kind), 1));
        line.Children.Add(Part(entry.Text, "LogTextStyle", 2));
        return line;
    }

    private TextBlock Part(string text, string style, int column)
    {
        var part = new TextBlock { Text = text, Style = (Style)Resources[style] };
        Grid.SetColumn(part, column);
        return part;
    }

    private static string KindStyle(string kind) => kind switch
    {
        "ERROR" => "LogErrorStyle",
        "WARN" => "LogWarnStyle",
        "OPEN" => "LogOpenStyle",
        "INFO" => "LogInfoStyle",
        _ => "LogWindowStyle",
    };
}
