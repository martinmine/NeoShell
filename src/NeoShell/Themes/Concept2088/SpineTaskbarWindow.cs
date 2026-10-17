using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Network;
using NeoShell.Interop.Windowing;
using NeoShell.QuickSettings;
using NeoShell.Settings;
using NeoShell.Taskbar;
using NeoShell.Tray;
using Windows.Graphics;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace NeoShell.Themes.Concept2088;

/// <summary>
/// The 2088 concept's taskbar, the Spine (see docs/design/theme-2088.md): the Nexus block that opens Start, the ask
/// field, a numbered chip per window and pinned app, the sensor cluster (tray icons and the network, volume and power
/// as readouts rather than icons) and the clock.
/// </summary>
internal sealed class SpineTaskbarWindow : Window, ITaskbarView
{
    public const int BarHeight = 48;
    private const double ChipWidth = 188;
    private const double ChipMinWidth = 58;
    private const double PinWidth = 44;
    private const double ChipGap = 4;

    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly TaskbarDock _dock;
    private readonly TaskButtonList _tasks;
    private readonly NotificationArea? _tray;
    private readonly TrayIconInput? _trayInput;
    private readonly Indicators? _indicators;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly Grid _root = new() { RequestedTheme = ElementTheme.Dark };
    private readonly Canvas _ruler = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Height = 6 };
    private readonly Grid _taskArea = new();
    private readonly StackPanel _taskPanel = new() { Orientation = Orientation.Horizontal, Spacing = ChipGap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<TaskButton, Chip> _chips = [];
    private readonly StackPanel _trayPanel = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> _trayButtons = [];
    private readonly Rectangle _traySeparator = new() { Width = 1, Height = 22, Fill = Hud.Solid(Hud.Line) };
    private bool _trayExpanded;
    private readonly HudFrame _startFrame = new(10, Hud.Red, Hud.Red) { Width = 108, Height = 36, MarkerLength = 0 };
    private readonly TextBlock _startGlyph = Hud.Label("起", 22, Hud.Text, Hud.Japanese, bold: true);
    private readonly TextBlock _startName = Hud.Label("NEXUS", 13, Hud.Text, spacing: 220, bold: true);
    private readonly TextBlock _startSub = Hud.Label("起動 · START", 8, Hud.Text, Hud.Japanese, spacing: 100);
    private readonly TextBlock _net = Readout();
    private readonly TextBlock _volume = Readout();
    private readonly TextBlock _power = Readout();
    private readonly FrameworkElement _powerReadout;
    private readonly TextBlock _time = Hud.Label("", 18, Hud.Text, bold: true);
    private readonly TextBlock _seconds = Hud.Label("", 10, Hud.Red, Hud.Mono);
    private readonly TextBlock _date = Hud.Label("", 9, Hud.Dim, Hud.Mono, spacing: 60);
    private readonly Rectangle _clockMark = new() { Height = 2, Fill = Hud.Solid(Hud.Red), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
    private readonly Button _clockButton;
    private readonly Button _askField;
    private bool _startOpen;
    private bool _startHot;

    public SpineTaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings)
    {
        _owner = owner;
        _monitor = monitor;
        _root.Background = Hud.Solid(Hud.Void);
        Content = _root;
        _dock = new TaskbarDock(this, owner, monitor, settings, BarHeight, busy: () => false, hiding: () => { });
        _tasks = new TaskButtonList(owner.Tracker);
        _tray = monitor.IsPrimary ? owner.Tray : null;
        _trayInput = _tray is null ? null : new TrayIconInput(_tray);
        _indicators = monitor.IsPrimary ? owner.Indicators : null;
        _clockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();

        var columns = new Grid();
        foreach (GridLength width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Add(columns, StartButton(), 0);
        _askField = AskField();
        SetSearchMode(owner.SearchMode);
        Add(columns, _askField, 1);
        _taskArea.Children.Add(_taskPanel);
        _taskArea.SizeChanged += (_, _) => LayoutChips();
        _taskArea.Clip = new RectangleGeometry();
        _taskArea.SizeChanged += (_, e) => ((RectangleGeometry)_taskArea.Clip).Rect = new(0, 0, e.NewSize.Width, e.NewSize.Height);
        Add(columns, _taskArea, 2);
        _powerReadout = Sensor("PWR", "電力", _power, "PowerReadout", () => Launcher.OpenSettings(_owner.RunMode, "Power", "ms-settings:batterysaver", "powercfg.cpl"));
        if (monitor.IsPrimary)
            Add(columns, Sensors(), 3);
        _clockButton = ClockButton();
        Add(columns, _clockButton, 4);
        Add(columns, ShowDesktopStrip(), 5);

        // The top edge: a hairline, a ruler's ticks along it and a red mark over the Nexus block.
        _root.Children.Add(columns);
        _root.Children.Add(new Rectangle { Height = 1, Fill = Hud.Solid(Hud.Line), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        _root.Children.Add(_ruler);
        _root.Children.Add(new Rectangle { Height = 2, Width = 124, Fill = Hud.Solid(Hud.Red), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        _root.SizeChanged += (_, e) => Hud.DrawRuler(_ruler, e.NewSize.Width, Hud.Faint);
        _root.KeyDown += Root_KeyDown;
        // A press on the bar's empty part closes Start (the taskbar doesn't take the focus, so Start keeps it).
        _root.PointerPressed += (_, _) => _owner.HideStartMenu();

        if (_tray is not null)
        {
            _tray.PromotedIcons.CollectionChanged += OnTrayChanged;
            _tray.OverflowIcons.CollectionChanged += OnTrayChanged;
            foreach (TrayIcon icon in AllTrayIcons())
                icon.PropertyChanged += OnTrayIconChanged;
        }
        if (_indicators is not null)
            _indicators.Changed += UpdateSensors;

        Closed += (_, _) =>
        {
            if (_tray is not null)
            {
                _tray.PromotedIcons.CollectionChanged -= OnTrayChanged;
                _tray.OverflowIcons.CollectionChanged -= OnTrayChanged;
                foreach (TrayIcon icon in AllTrayIcons())
                    icon.PropertyChanged -= OnTrayIconChanged;
            }
            if (_indicators is not null)
                _indicators.Changed -= UpdateSensors;
            foreach (TaskButton button in _chips.Keys)
                button.PropertyChanged -= OnTaskChanged;
            _trayInput?.Stop();
            _clockTimer.Stop();
        };
        _root.Loaded += (_, _) => RefreshTasks();
        UpdateClock();
        _clockTimer.Start();
        UpdateSensors();
        RefreshTray();
    }

    public DisplayMonitor Monitor => _monitor;

    public nint Handle => _dock.Handle;

    public RectInt32 ScreenBounds => _dock.ScreenBounds;

    public void Show() => AppWindow.Show(activateWindow: false);

    public void Shut()
    {
        _dock.Shut();
        Close();
    }

    public void Reveal() => _dock.Reveal();

    /// <summary>Start opened or closed above this taskbar: the Nexus block turns to an outline while it's open.</summary>
    public void ShowStartOpen(bool open)
    {
        _startOpen = open;
        UpdateStartLook();
    }

    // The concept has one look of its own: Windows' light or dark mode, its accent colour and the backdrop don't change it.
    public void SetTheme(ElementTheme theme, Color? accent) { }

    public void SetBackdrop(Backdrop kind) { }

    public void UpdateClock()
    {
        DateTime now = DateTime.Now;
        _time.Text = now.ToString("HH:mm");
        _seconds.Text = now.ToString(":ss");
        _date.Text = $"{Clock2088.Date(now)} {Clock2088.Weekday(now)}";
        ToolTipService.SetToolTip(_clockButton, now.ToLongDateString());
    }

    public void ShowClockOpen(bool open) => _clockMark.Opacity = open ? 1 : 0;

    // Explorer's search setting: hidden takes the ask field away (Nexus still opens with its ask line); an icon or a
    // box both show it, it has one size.
    public void SetSearchMode(TaskbarSearchMode mode) =>
        _askField.Visibility = mode == TaskbarSearchMode.Hidden ? Visibility.Collapsed : Visibility.Visible;

    // Quick Settings, the input switcher and the Quick Link menu are Windows 11's.
    public void ShowQuickSettings(QuickSettingsPage page) { }

    public void PrepareQuickSettings() { }

    public void RunInputSwitch(InputSwitchCommand command) { }

    public void ToggleQuickLinks() { }

    public void ShowJumpList(int index) { }

    public void SetFullScreenWindow(nint window) => _dock.SetFullScreenWindow(window);

    public RectInt32? TaskButtonBounds(nint hwnd) =>
        _tasks.Of(hwnd) is { } button && _chips.TryGetValue(button, out Chip? chip) && chip.Button.ActualWidth > 0
            ? _dock.BoundsOnScreen(chip.Button) : null;

    public RectInt32? TrayIconBounds(TrayIcon icon) =>
        _trayButtons.TryGetValue(icon.Key, out Button? button) && button.ActualWidth > 0 ? _dock.BoundsOnScreen(button) : null;

    public void ActivateTask(int index)
    {
        if (_tasks.At(index) is not { } button)
            return;
        Reveal();
        if (TaskButtonList.ClickActs(button))
            _tasks.Click(button);
        else
            _tasks.ActivateNext(button);
    }

    public void LaunchTask(int index, bool elevated)
    {
        if (_tasks.At(index) is { } button)
            Launcher.Launch(button.App, elevated);
    }

    public void ActivateLastWindow(int index)
    {
        if (_tasks.At(index) is { } button)
            _tasks.ActivateLastWindow(button);
    }

    /// <summary>Win+T: the keyboard on the first (or last) chip.</summary>
    public void FocusTaskList(bool last = false)
    {
        if (_tasks.Count == 0)
            return;
        _dock.TakeKeyboard();
        if (_chips.TryGetValue(last ? _tasks[^1] : _tasks[0], out Chip? chip))
            chip.Button.Focus(FocusState.Keyboard);
    }

    /// <summary>Win+B: the keyboard on the first tray icon.</summary>
    public void FocusTray()
    {
        if (_trayPanel.Children.OfType<Button>().FirstOrDefault() is not { } first)
            return;
        _dock.TakeKeyboard();
        first.Focus(FocusState.Keyboard);
    }

    /// <summary>Every window gets its own chip, after the pinned apps, as Win+1…9 count them.</summary>
    public void RefreshTasks()
    {
        IReadOnlyList<TaskButtonModel> models = TaskOrder.Arrange(
            TaskListBuilder.Build(_owner.Settings.Current.PinnedTaskbarApps, _owner.Tracker.Windows, combine: false), _owner.TaskOrder);
        _owner.TaskOrder = [.. models.Select(model => model.Key)];
        _tasks.Update(models, combine: false);

        foreach (TaskButton gone in _chips.Keys.Where(button => !_tasks.Contains(button)).ToList())
        {
            gone.PropertyChanged -= OnTaskChanged;
            _chips.Remove(gone);
        }
        for (int i = 0; i < _tasks.Count; i++)
        {
            TaskButton button = _tasks[i];
            if (!_chips.TryGetValue(button, out Chip? chip))
            {
                _chips[button] = chip = new Chip(this, button);
                button.PropertyChanged += OnTaskChanged;
            }
            chip.Update(i);
        }
        // Only when the order changed: a chip taken out and put back loses the pointer it's being pressed with.
        if (!_taskPanel.Children.SequenceEqual(_tasks.Select(button => (UIElement)_chips[button].Button)))
        {
            _taskPanel.Children.Clear();
            foreach (TaskButton button in _tasks)
                _taskPanel.Children.Add(_chips[button].Button);
        }
        LayoutChips();
    }

    public void RefreshTray()
    {
        if (_tray is null)
            return;

        IReadOnlyList<TrayIcon> shown = [.. _tray.PromotedIcons, .. _trayExpanded ? _tray.OverflowIcons : []];
        foreach (string gone in _trayButtons.Keys.Where(key => !shown.Any(icon => icon.Key == key)).ToList())
            _trayButtons.Remove(gone);
        _trayPanel.Children.Clear();
        foreach (TrayIcon icon in shown)
        {
            if (!_trayButtons.TryGetValue(icon.Key, out Button? button))
                _trayButtons[icon.Key] = button = TrayButton(icon);
            ((Image)((Grid)button.Content).Children[0]).Source = icon.Icon;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, icon.Name);
            ToolTipService.SetToolTip(button, icon.ToolTip);
            _trayPanel.Children.Add(button);
        }
        // The hidden icons fold into a count that unfolds them in place.
        int hidden = _tray.OverflowIcons.Count;
        if (hidden > 0)
        {
            var more = Hud.Plain("TrayChevron", _trayExpanded ? "Fewer icons" : "Show hidden icons",
                new Grid { Width = 26, Height = 24, Children = { Hud.Label(_trayExpanded ? "‹" : $"+{hidden}", 10, Hud.Red, Hud.Mono) } });
            ((TextBlock)((Grid)more.Content).Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            more.Click += (_, _) =>
            {
                _trayExpanded = !_trayExpanded;
                RefreshTray();
            };
            _trayPanel.Children.Add(more);
        }
        _trayPanel.Visibility = _traySeparator.Visibility = _trayPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<TrayIcon> AllTrayIcons() => _tray is null ? [] : _tray.PromotedIcons.Concat(_tray.OverflowIcons);

    private static void Add(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock Readout() => Hud.Label("", 12, Hud.Text, Hud.Mono);

    private Button StartButton()
    {
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_startName);
        text.Children.Add(_startSub);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_startGlyph);
        content.Children.Add(text);
        _startFrame.Children.Add(content);

        Button button = Hud.Plain("StartButton", "Start", _startFrame);
        button.Margin = new Thickness(8, 6, 8, 6);
        button.Click += (_, _) => _owner.ToggleStartMenu(this);
        button.PointerEntered += (_, _) => { _startHot = true; UpdateStartLook(); };
        button.PointerExited += (_, _) => { _startHot = false; UpdateStartLook(); };
        return button;
    }

    private void UpdateStartLook()
    {
        // Open, the block turns to an outline: the panel above has taken its colour.
        Color fill = _startOpen ? Hud.Void : _startHot ? Hud.RedHot : Hud.Red;
        Color text = _startOpen ? Hud.Red : Hud.Text;
        _startFrame.SetLook(fill, Hud.Red);
        _startGlyph.Foreground = _startName.Foreground = _startSub.Foreground = Hud.Solid(text);
    }

    // What the 2088 desktop is driven by: say or type what you want. A click opens Nexus with its ask line.
    private Button AskField()
    {
        var frame = new HudFrame(8, Hud.Raised, Hud.Line) { Width = 236, Height = 36, MarkerLength = 0 };
        var caret = Hud.Label("▌", 13, Hud.Red, Hud.Mono);
        var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.1) };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.55), Value = 0 });
        Storyboard.SetTarget(blink, caret);
        Storyboard.SetTargetProperty(blink, "Opacity");
        var story = new Storyboard { Children = { blink } };
        caret.Loaded += (_, _) => story.Begin();

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Hud.Label("›", 16, Hud.Red, Hud.Mono, bold: true));
        line.Children.Add(Hud.Label("ASK NEXUS", 12, Hud.Dim, spacing: 160));
        line.Children.Add(caret);
        frame.Children.Add(line);
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Spacing = 3 };
        hint.Children.Add(Hud.Label("意図", 9, Hud.Faint, Hud.Japanese));
        hint.Children.Add(Hud.Label("WIN+S", 8, Hud.Faint, Hud.Mono));
        frame.Children.Add(hint);

        Button button = Hud.Plain("SearchBox", "Ask Nexus", frame);
        button.Margin = new Thickness(0, 6, 8, 6);
        button.Click += (_, _) => _owner.ToggleStartMenu(this);
        button.PointerEntered += (_, _) => frame.SetLook(Hud.Raised, Hud.LineBright);
        button.PointerExited += (_, _) => frame.SetLook(Hud.Raised, Hud.Line);
        return button;
    }

    private FrameworkElement Sensors()
    {
        var frame = new HudFrame(6, Hud.Panel, Hud.Line) { Height = 36, Margin = new Thickness(4, 6, 6, 6), MarkerLength = 6 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_trayPanel);
        row.Children.Add(_traySeparator);
        row.Children.Add(Sensor("NET", "網", _net, "NetworkReadout", () => Launcher.OpenSettings(_owner.RunMode, "Network", "ms-settings:network", "ncpa.cpl")));
        FrameworkElement volume = Sensor("VOL", "音", _volume, "VolumeReadout", ToggleMute);
        // A turn of the wheel over the readout sets the volume, 2% a notch.
        volume.PointerWheelChanged += (_, e) =>
        {
            if (_indicators is null)
                return;
            int notches = e.GetCurrentPoint(volume).Properties.MouseWheelDelta / 120;
            _indicators.Volume = Math.Clamp(_indicators.Volume + notches * 0.02f, 0, 1);
            UpdateSensors();
            e.Handled = true;
        };
        row.Children.Add(volume);
        row.Children.Add(_powerReadout);
        frame.Children.Add(row);
        return frame;
    }

    private static FrameworkElement Sensor(string label, string japanese, TextBlock value, string automationId, Action click)
    {
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        caption.Children.Add(Hud.Label(label, 8, Hud.Faint, Hud.Mono, spacing: 100));
        caption.Children.Add(Hud.Label(japanese, 8, Hud.Red, Hud.Japanese));
        var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };
        stack.Children.Add(caption);
        stack.Children.Add(value);
        Button button = Hud.Plain(automationId, label, stack);
        button.Click += (_, _) => click();
        return button;
    }

    private void ToggleMute()
    {
        if (_indicators is { HasAudioDevice: true })
        {
            _indicators.IsMuted = !_indicators.IsMuted;
            UpdateSensors();
        }
    }

    private void UpdateSensors()
    {
        if (_indicators is not { } indicators)
            return;

        NetworkState network = indicators.Network;
        string bars = new string('▮', network.SignalBars) + new string('▯', 5 - network.SignalBars);
        (_net.Text, Color netColor) = network.Kind switch
        {
            NetworkKind.Disconnected => ("NO LINK", Hud.Red),
            _ when !network.HasInternet => ("LOCAL", Hud.Red),
            NetworkKind.WiFi => (bars, Hud.Text),
            NetworkKind.Cellular => ("CELL " + bars[..3], Hud.Text),
            NetworkKind.Ethernet => ("ETH·OK", Hud.Text),
            _ => ("LINK", Hud.Text),
        };
        _net.Foreground = Hud.Solid(netColor);

        bool muted = !indicators.HasAudioDevice || indicators.IsMuted;
        _volume.Text = !indicators.HasAudioDevice ? "--" : indicators.IsMuted ? "MUTE" : $"{indicators.Volume * 100:0}%";
        _volume.Foreground = Hud.Solid(muted ? Hud.Red : Hud.Text);

        _powerReadout.Visibility = indicators.Battery is null ? Visibility.Collapsed : Visibility.Visible;
        if (indicators.Battery is { } battery)
        {
            _power.Text = $"{battery.Percent}%{(battery.IsCharging ? "+" : "")}";
            _power.Foreground = Hud.Solid(battery.Percent <= 15 && !battery.IsCharging ? Hud.Red : Hud.Text);
        }
    }

    private Button ClockButton()
    {
        var time = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, HorizontalAlignment = HorizontalAlignment.Right };
        time.Children.Add(_time);
        _seconds.VerticalAlignment = VerticalAlignment.Bottom;
        time.Children.Add(_seconds);
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 12, 0) };
        stack.Children.Add(time);
        stack.Children.Add(_date);
        var grid = new Grid { Children = { stack, _clockMark } };
        _clockMark.Margin = new Thickness(10, 0, 12, 4);

        Button button = Hud.Plain("ClockButton", "Clock", grid);
        button.Click += (_, _) => _owner.ToggleClockFlyout(this);
        return button;
    }

    private Button ShowDesktopStrip()
    {
        var mark = new Rectangle { Width = 2, Height = 22, Fill = Hud.Solid(Hud.Faint), HorizontalAlignment = HorizontalAlignment.Center };
        Button button = Hud.Plain("ShowDesktopButton", "Show desktop", new Grid { Width = 12, Background = Hud.Solid(Hud.Void), Children = { mark } });
        button.Click += (_, _) => _owner.ToggleDesktop();
        button.PointerEntered += (_, _) => mark.Fill = Hud.Solid(Hud.Red);
        button.PointerExited += (_, _) => mark.Fill = Hud.Solid(Hud.Faint);
        ToolTipService.SetToolTip(button, "SHOW DESKTOP // デスクトップ");
        return button;
    }

    private Button TrayButton(TrayIcon icon)
    {
        var content = new Grid { Width = 24, Height = 24, Children = { new Image { Width = 16, Height = 16, IsHitTestVisible = false } } };
        Button button = Hud.Plain("TrayIcon", icon.Name, content);
        button.PointerEntered += (_, _) => _trayInput?.Enter(icon);
        button.PointerExited += (_, _) => _trayInput?.EndHover();
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            _owner.HideStartMenu();
            if (_trayInput!.PressEvent(icon, e.GetCurrentPoint(button).Properties) is { } pressed)
                _trayInput.Press(icon, pressed);
        }), handledEventsToo: true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (TrayIconInput.ReleaseEvent(e.GetCurrentPoint(button).Properties.PointerUpdateKind) is { } released)
                _trayInput!.Release(icon, released);
        }), handledEventsToo: true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => _trayInput?.Move(icon)), handledEventsToo: true);
        return button;
    }

    private void OnTrayChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (TrayIcon icon in e.OldItems?.OfType<TrayIcon>() ?? [])
            icon.PropertyChanged -= OnTrayIconChanged;
        foreach (TrayIcon icon in e.NewItems?.OfType<TrayIcon>() ?? [])
            icon.PropertyChanged += OnTrayIconChanged;
        RefreshTray();
    }

    private void OnTrayIconChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshTray();

    private void OnTaskChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TaskButton button && _chips.TryGetValue(button, out Chip? chip))
            chip.Update(_tasks.IndexOf(button));
    }

    // Chips start at their full width and narrow together when the row is full, down to the index and the icon.
    private void LayoutChips()
    {
        double room = _taskArea.ActualWidth;
        int windows = _tasks.Count(button => button.Windows.Count > 0);
        double fixedWidth = (_tasks.Count - windows) * PinWidth + Math.Max(0, _tasks.Count - 1) * ChipGap;
        double width = windows == 0 ? ChipWidth : Math.Clamp((room - fixedWidth) / windows, ChipMinWidth, ChipWidth);
        foreach ((TaskButton button, Chip chip) in _chips)
            chip.SetWidth(button.Windows.Count > 0 ? width : PinWidth);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Space and Enter press the focused button (the Button does); Escape leaves the taskbar.
        if (e.Key == VirtualKey.Escape)
        {
            TopLevelWindows.Activate(_owner.Tracker.Foreground);
            e.Handled = true;
        }
    }

    /// <summary>
    /// One task chip: its Win+N index, the icon and the window's title in capitals, with a bar along the bottom for
    /// running (grey), in front (red, with edge markers) and progress.
    /// </summary>
    private sealed class Chip
    {
        private readonly SpineTaskbarWindow _bar;
        private readonly TaskButton _task;
        private readonly HudFrame _frame = new(7, Hud.Panel, Hud.Line) { Height = 36, MarkerLength = 0 };
        private readonly TextBlock _index = Hud.Label("", 9, Hud.Faint, Hud.Mono);
        private readonly Image _icon = new() { Width = 20, Height = 20, IsHitTestVisible = false };
        private readonly TextBlock _title = Hud.Label("", 12, Hud.Dim, spacing: 60);
        private readonly Rectangle _state = new() { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 3) };
        private readonly Rectangle _progress = new() { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 3), Fill = Hud.Solid(Hud.Text) };
        private bool _hot;

        public Chip(SpineTaskbarWindow bar, TaskButton task)
        {
            _bar = bar;
            _task = task;
            var row = new Grid { Margin = new Thickness(8, 0, 8, 0), ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.TextWrapping = TextWrapping.NoWrap;
            Add(row, _index, 0);
            Add(row, _icon, 1);
            Add(row, _title, 2);
            _frame.Children.Add(row);
            _frame.Children.Add(_state);
            _frame.Children.Add(_progress);

            Button = Hud.Plain("TaskButton", task.Title, _frame);
            Button.Click += (_, _) => Click();
            Button.PointerEntered += (_, _) => { _hot = true; Paint(); };
            Button.PointerExited += (_, _) => { _hot = false; Paint(); };
            Button.ContextRequested += (_, e) =>
            {
                Menu().ShowAt(Button);
                e.Handled = true;
            };
        }

        public Button Button { get; }

        public void SetWidth(double width)
        {
            Button.Width = width;
            _title.Visibility = _task.Windows.Count > 0 && width >= 96 ? Visibility.Visible : Visibility.Collapsed;
            _index.Visibility = width >= ChipMinWidth ? Visibility.Visible : Visibility.Collapsed;
            Paint();
        }

        public void Update(int index)
        {
            _index.Text = index < 9 ? $"{index + 1:00}" : "··";
            _icon.Source = _task.Icon;
            _title.Text = _task.Title.ToUpperInvariant();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Button, _task.Title);
            ToolTipService.SetToolTip(Button, _task.Title);
            Paint();
        }

        private void Paint()
        {
            bool running = _task.Windows.Count > 0;
            (Color fill, Color stroke, Color title, Color index, Color state) = _task switch
            {
                { ShowsAttention: true } => (Hud.RedDeep, Hud.Red, Hud.Text, Hud.Text, Hud.RedHot),
                { IsActive: true } => (Hud.Raised, Hud.Red, Hud.Text, Hud.Red, Hud.Red),
                _ when running => (Hud.Panel, _hot ? Hud.LineBright : Hud.Line, _hot ? Hud.Text : Hud.Dim, Hud.Faint, Hud.Faint),
                _ => (Hud.Void, _hot ? Hud.LineBright : Hud.Line, Hud.Dim, Hud.Faint, Colors.Transparent),
            };
            _frame.SetLook(fill, stroke, Hud.Red);
            _frame.MarkerLength = _task.IsActive ? 6 : 0;
            _title.Foreground = Hud.Solid(title);
            _index.Foreground = Hud.Solid(index);
            _state.Fill = Hud.Solid(state);
            _icon.Opacity = running || _hot ? 1 : 0.6;
            double room = Math.Max(0, Button.Width - 16);
            _progress.Width = _task.ShowsProgress ? room * _task.ProgressPercent / 100 : 0;
            _progress.Fill = Hud.Solid(_task.IsProgressError ? Hud.Red : _task.IsProgressPaused ? Hud.Dim : Hud.Text);
        }

        private void Click()
        {
            _bar._owner.HideStartMenu();
            if (TaskButtonList.ClickActs(_task))
                _bar._tasks.Click(_task);
            else
                _bar._tasks.ActivateNext(_task);
        }

        private MenuFlyout Menu()
        {
            var menu = new MenuFlyout();
            menu.Items.Add(new MenuFlyoutItem { Text = _task.App.DisplayName.ToUpperInvariant(), IsEnabled = false });
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("LAUNCH NEW // 新規起動", "LaunchNewMenuItem", () => Launcher.Launch(_task.App)));
            if (_task.Pinned is { } pinned)
                menu.Items.Add(Item("UNPIN // 固定解除", "UnpinMenuItem", () => _bar._owner.Unpin(pinned)));
            else
                menu.Items.Add(Item("PIN // 固定", "PinMenuItem", () => _bar._owner.Pin(_task.App)));
            if (_task.Windows.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(Item(_task.Windows.Count > 1 ? "CLOSE ALL // 全て閉じる" : "CLOSE // 閉じる", "CloseWindowMenuItem", () =>
                {
                    foreach (WindowInfo window in _task.Windows)
                        TopLevelWindows.Close(window.Handle);
                }));
            }
            return menu;
        }

        private static MenuFlyoutItem Item(string text, string automationId, Action click)
        {
            var item = new MenuFlyoutItem { Text = text, FontFamily = Hud.Display, CharacterSpacing = 80 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, automationId);
            item.Click += (_, _) => click();
            return item;
        }
    }
}
