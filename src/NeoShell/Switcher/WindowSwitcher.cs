using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Snap;
using NeoShell.Taskbar;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Switcher;

/// <summary>
/// Alt+Tab as the shell: live previews of the open windows in rows on an acrylic panel in the middle of the screen,
/// most recently used first, the chosen one outlined. Driven by <see cref="AltTabKeys"/>; a click on a preview
/// switches to it, its close button (or Delete) closes it.
/// </summary>
internal sealed class WindowSwitcher : Window
{
    // Effective pixels, as Explorer's switcher on a 100% screen: cards with the window's title over its preview, all
    // previews the same height, the chosen card ringed in the accent colour.
    private const double PreviewHeight = 132;
    private const double MinPreviewHeight = 60;
    private const double MinCardWidth = 160;
    private const double HeaderHeight = 36;
    private const double CardInset = 4;
    private const double Spacing = 24;
    private const double RingGap = 3;
    private const double RingThickness = 3;
    private const double PanelPadding = 48;
    private const double ScreenMargin = 96;
    // A quick Alt+Tab switches without the panel flashing up.
    private static readonly TimeSpan s_showDelay = TimeSpan.FromMilliseconds(100);

    private readonly WindowTracker _tracker;
    private readonly Func<ElementTheme> _theme;
    private readonly WindowSnapping? _snapping;
    private readonly nint _hwnd;
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly Canvas _canvas = new();
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly DispatcherQueueTimer _showTimer;
    private readonly List<Item> _items = [];
    private IReadOnlyList<SwitcherSlot> _slots = [];
    private int _selected = -1;
    private bool _visible;

    /// <param name="theme">The theme the taskbar shows, read each time the switcher opens.</param>
    /// <param name="snapping">Snap, whose snap groups the switcher shows as items of their own.</param>
    public WindowSwitcher(WindowTracker tracker, Func<ElementTheme> theme, WindowSnapping? snapping)
    {
        _tracker = tracker;
        _theme = theme;
        _snapping = snapping;
        Content = _canvas;
        SystemBackdrop = _backdrop;
        // Explorer's switcher's name, for UI Automation.
        Title = "Task Switching";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of its own list, and the app in front keeps the focus: the keys come from the keyboard hook.
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        Peek.Exclude(_hwnd);
        _frameless = new FramelessWindow(_hwnd, roundedCorners: true);
        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Topmost);
        _showTimer = DispatcherQueue.CreateTimer();
        _showTimer.Interval = s_showDelay;
        _showTimer.IsRepeating = false;
        _showTimer.Tick += (_, _) => ShowPanel();
        tracker.Changed += OnWindowsChanged;

        Closed += (_, _) =>
        {
            tracker.Changed -= OnWindowsChanged;
            _showTimer.Stop();
            Clear();
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    /// <summary>The switcher closed by itself: a preview was clicked, or the last window closed.</summary>
    public event Action? Dismissed;

    public void Run(SwitcherCommand command)
    {
        switch (command)
        {
            case SwitcherCommand.Open or SwitcherCommand.OpenBackwards:
                Open(backwards: command == SwitcherCommand.OpenBackwards);
                break;
            case SwitcherCommand.Next:
                Select(AltTabLayout.Step(_selected, _items.Count, 1));
                break;
            case SwitcherCommand.Previous:
                Select(AltTabLayout.Step(_selected, _items.Count, -1));
                break;
            case SwitcherCommand.Up:
                Select(AltTabLayout.Vertical(_slots, _selected, -1));
                break;
            case SwitcherCommand.Down:
                Select(AltTabLayout.Vertical(_slots, _selected, 1));
                break;
            case SwitcherCommand.Switch:
                SwitchTo(_selected);
                break;
            case SwitcherCommand.Cancel:
                Hide();
                break;
            case SwitcherCommand.CloseWindow:
                CloseWindow(_selected);
                break;
        }
    }

    private void Open(bool backwards)
    {
        int ownProcess = Environment.ProcessId;
        Dictionary<nint, WindowInfo> windows = _tracker.Windows.Where(w => w.ProcessId != ownProcess).ToDictionary(w => w.Handle);
        IReadOnlyList<nint> order = AltTabLayout.Order([.. windows.Keys], TopLevelWindows.GetAll(), TopLevelWindows.GetForeground());
        // "Show my snapped windows ... when I press Alt+Tab": each snap group before its windows, as Explorer's.
        IReadOnlyList<SwitcherEntry> entries = _snapping is not null && WindowSnapping.ShowGroups
            ? AltTabLayout.WithGroups(order, _snapping.Groups.All)
            : [.. order.Select(hwnd => new SwitcherEntry(hwnd))];

        Clear();
        foreach (SwitcherEntry entry in entries)
            _items.Add(new Item(windows[entry.Window], entry.Group));
        _selected = AltTabLayout.FirstSelection(entries, backwards);
        _showTimer.Start();
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        _selected = index;
        // Moving on is a sign of looking for a window: the panel shows straight away.
        if (!_visible)
            ShowPanel();
        else
            ShowSelection();
    }

    private void SwitchTo(int index)
    {
        Item? item = index >= 0 && index < _items.Count ? _items[index] : null;
        Hide();
        if (item?.Group is { } group)
            WindowSnapping.ActivateGroup(group);
        else if (item is not null)
            TopLevelWindows.SwitchTo(item.Window.Handle);
    }

    private void Hide()
    {
        _showTimer.Stop();
        if (_visible)
        {
            _visible = false;
            AppWindow.Hide();
        }
        Clear();
    }

    private void CloseWindow(int index)
    {
        // A group's item closes nothing.
        if (index < 0 || index >= _items.Count || _items[index].Group is not null)
            return;

        TopLevelWindows.Close(_items[index].Window.Handle);
        Remove(index);
    }

    private void Remove(int index)
    {
        _items[index].Thumbnail?.Dispose();
        _items[index].GroupPreview?.Dispose();
        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            Hide();
            Dismissed?.Invoke();
            return;
        }
        _selected = Math.Min(_selected, _items.Count - 1);
        if (_visible)
            ShowPanel();
    }

    // Windows that closed while the switcher is up leave it.
    private void OnWindowsChanged()
    {
        if (_items.Count == 0)
            return;

        var open = _tracker.Windows.Select(w => w.Handle).ToHashSet();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!open.Contains(_items[i].Window.Handle))
                Remove(i);
        }
    }

    // Lays the previews out for the monitor the window in front is on, and shows the panel there.
    private void ShowPanel()
    {
        _showTimer.Stop();
        if (_items.Count == 0)
            return;

        DisplayMonitor monitor = MonitorInFront();
        double scale = monitor.Dpi / 96.0;
        ElementTheme theme = _theme();
        _canvas.RequestedTheme = theme;
        _backdrop.Theme = theme;
        foreach (Item item in _items)
        {
            if (item.Group is { } group && _snapping is not null)
                item.GroupPreview ??= new SnapGroupPreview(_hwnd, group, _snapping);
            else
                item.Thumbnail ??= TryRegister(item.Window.Handle);
        }

        // As many rows as the screen takes; with more, the previews get smaller (Explorer's scroll instead).
        double maxGridWidth = monitor.WorkArea.Width / scale - 2 * (ScreenMargin + PanelPadding);
        double maxGridHeight = monitor.WorkArea.Height / scale - 2 * (ScreenMargin + PanelPadding);
        double previewHeight = PreviewHeight;
        while (true)
        {
            double height = previewHeight;
            (IReadOnlyList<SwitcherSlot> slots, double gridWidth) = AltTabLayout.Arrange(
                [.. _items.Select(item => CardWidth(item, height))], maxGridWidth, Spacing);
            int rows = slots.Count == 0 ? 0 : slots.Max(s => s.Row) + 1;
            double gridHeight = rows * CardHeight(previewHeight) + (rows - 1) * Spacing;
            if (gridHeight <= maxGridHeight || previewHeight <= MinPreviewHeight)
            {
                _slots = slots;
                Build(previewHeight, theme, scale);
                Place(monitor, gridWidth + 2 * PanelPadding, gridHeight + 2 * PanelPadding, scale);
                break;
            }
            previewHeight = Math.Max(MinPreviewHeight, previewHeight * 0.85);
        }
        ShowSelection();
        if (!_visible)
        {
            _visible = true;
            AppWindow.Show(activateWindow: false);
        }
    }

    private static double CardHeight(double previewHeight) => HeaderHeight + previewHeight + CardInset;

    private static double CardWidth(Item item, double previewHeight) =>
        Math.Max(MinCardWidth, PreviewWidth(item, previewHeight) + 2 * CardInset);

    // The window's shape at the preview's height; a minimized window shows its icon in a preview of the usual shape. A
    // group's is its screen's.
    private static double PreviewWidth(Item item, double height)
    {
        if (item.GroupPreview is { } group)
            return height * group.Aspect;
        SizeInt32 source = item.Thumbnail?.SourceSize ?? default;
        if (TopLevelWindows.IsMinimized(item.Window.Handle) || source.Width <= 0 || source.Height <= 0)
            return height * 16 / 10;
        return Math.Clamp(height * source.Width / source.Height, height * 0.6, height * 2.4);
    }

    private void Build(double previewHeight, ElementTheme theme, double scale)
    {
        _canvas.Children.Clear();
        bool dark = theme == ElementTheme.Dark;
        // A window's colours, as Explorer's cards look like title bars; colours rather than the app's brushes, which
        // would follow the app's theme rather than the panel's.
        var card = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        var hover = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D) : Color.FromArgb(0xFF, 0xFB, 0xFB, 0xFB));
        var ring = new SolidColorBrush((Color)Application.Current.Resources[dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1"]);
        var transparent = new SolidColorBrush(Colors.Transparent);
        // The ring stands a little off the card, as Explorer's does.
        const double ringOffset = RingGap + RingThickness;

        for (int i = 0; i < _items.Count; i++)
        {
            Item item = _items[i];
            SwitcherSlot slot = _slots[i];
            double x = PanelPadding + slot.X;
            double y = PanelPadding + slot.Row * (CardHeight(previewHeight) + Spacing);
            double height = CardHeight(previewHeight);

            var selection = new Border
            {
                Width = slot.Width + 2 * ringOffset,
                Height = height + 2 * ringOffset,
                CornerRadius = new CornerRadius(8 + ringOffset),
                BorderBrush = ring,
                BorderThickness = new Thickness(RingThickness),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Canvas.SetLeft(selection, x - ringOffset);
            Canvas.SetTop(selection, y - ringOffset);
            _canvas.Children.Add(selection);
            item.Ring = selection;

            var cell = new Grid
            {
                Width = slot.Width,
                Height = height,
                CornerRadius = new CornerRadius(8),
                Background = card,
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(HeaderHeight) },
                    new RowDefinition(),
                },
            };
            Canvas.SetLeft(cell, x);
            Canvas.SetTop(cell, y);
            AutomationProperties.SetName(cell, item.Window.Title);
            AutomationProperties.SetAutomationId(cell, "SwitcherItem");

            var header = new Grid { Padding = new Thickness(12, 0, 4, 0), ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // A group's title has no icon, as in Explorer.
            if (item.Group is null)
                header.Children.Add(new Image { Source = _tracker.WindowIcon(item.Window), Width = 16, Height = 16 });
            var title = new TextBlock
            {
                Text = item.Group is { } members ? GroupTitle(members) : item.Window.Title,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var close = new Button
            {
                Content = new FontIcon { Glyph = "\uE8BB", FontSize = 10 },
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = transparent,
                BorderThickness = new Thickness(0),
                // Only over the card, as in Explorer.
                Opacity = 0,
            };
            AutomationProperties.SetName(close, "Close " + item.Window.Title);
            AutomationProperties.SetAutomationId(close, "SwitcherCloseButton");
            Grid.SetColumn(close, 2);
            if (item.Group is null)
                header.Children.Add(close);
            cell.Children.Add(header);

            bool minimized = item.Group is null && TopLevelWindows.IsMinimized(item.Window.Handle);
            if (item.Group is null && (item.Thumbnail is null || minimized))
            {
                var icon = new Image { Source = _tracker.WindowIcon(item.Window), Width = 48, Height = 48 };
                Grid.SetRow(icon, 1);
                cell.Children.Add(icon);
            }

            Item current = item;
            cell.PointerEntered += (_, _) =>
            {
                cell.Background = hover;
                close.Opacity = 1;
            };
            cell.PointerExited += (_, _) =>
            {
                cell.Background = card;
                close.Opacity = 0;
            };
            cell.Tapped += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsWithin(source, close))
                    return;
                SwitchTo(_items.IndexOf(current));
                Dismissed?.Invoke();
            };
            close.Click += (_, _) => CloseWindow(_items.IndexOf(current));
            _canvas.Children.Add(cell);

            if (item.GroupPreview is { } groupPreview)
            {
                double groupWidth = previewHeight * groupPreview.Aspect;
                groupPreview.Show(new RectInt32(
                    (int)Math.Round((x + (slot.Width - groupWidth) / 2) * scale),
                    (int)Math.Round((y + HeaderHeight) * scale),
                    (int)Math.Round(groupWidth * scale),
                    (int)Math.Round(previewHeight * scale)));
            }

            // DWM draws the live preview over the card, under its title, in the window's pixels.
            if (item.Thumbnail is { } thumbnail && !minimized)
            {
                SizeInt32 source = thumbnail.SourceSize;
                double fit = Math.Min((slot.Width - 2 * CardInset) / source.Width, previewHeight / source.Height);
                double width = source.Width * fit;
                double top = y + HeaderHeight + (previewHeight - source.Height * fit) / 2;
                thumbnail.Show(new RectInt32(
                    (int)Math.Round((x + (slot.Width - width) / 2) * scale),
                    (int)Math.Round(top * scale),
                    (int)Math.Round(width * scale),
                    (int)Math.Round(source.Height * fit * scale)));
            }
        }
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Ring is { } ring)
                ring.Visibility = i == _selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Place(DisplayMonitor monitor, double width, double height, double scale)
    {
        int pixelWidth = (int)Math.Ceiling(width * scale);
        int pixelHeight = (int)Math.Ceiling(height * scale);
        RectInt32 area = monitor.Bounds;
        _placement.Bounds = new RectInt32(
            area.X + (area.Width - pixelWidth) / 2, area.Y + (area.Height - pixelHeight) / 2, pixelWidth, pixelHeight);
    }

    private static DisplayMonitor MonitorInFront()
    {
        nint handle = TopLevelWindows.MonitorOf(TopLevelWindows.GetForeground());
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        return monitors.FirstOrDefault(m => m.Handle == handle)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors[0];
    }

    // Some windows (elevated ones) can't be previewed; they show their icon.
    private DwmThumbnail? TryRegister(nint window)
    {
        try
        {
            return new DwmThumbnail(_hwnd, window);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Clear()
    {
        foreach (Item item in _items)
        {
            item.Thumbnail?.Dispose();
            item.GroupPreview?.Dispose();
        }
        _items.Clear();
        _slots = [];
        _selected = -1;
        _canvas.Children.Clear();
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e == ancestor)
                return true;
        }
        return false;
    }

    private string GroupTitle(IReadOnlyList<nint> group)
    {
        Dictionary<nint, WindowInfo> windows = _tracker.Windows.ToDictionary(w => w.Handle);
        return SnapGroups.Title([.. group.Select(h => windows.TryGetValue(h, out WindowInfo? w) ? w.Title : "")]);
    }

    /// <summary>A window's item, or a snap group's (<see cref="Window"/> is then its most recently used).</summary>
    private sealed class Item(WindowInfo window, IReadOnlyList<nint>? group)
    {
        public WindowInfo Window { get; } = window;
        public IReadOnlyList<nint>? Group { get; } = group;
        public SnapGroupPreview? GroupPreview { get; set; }
        public DwmThumbnail? Thumbnail { get; set; }
        public Border? Ring { get; set; }
    }
}
