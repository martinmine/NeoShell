using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.QuickSettings;
using NeoShell.Settings;
using NeoShell.Taskbar;
using NeoShell.Tray;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;
using static NeoShell.Themes.WindowsXp.XpTaskbarLayout;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Windows XP's taskbar on one monitor (see docs/design/theme-xp.md): the green Start button, Quick Launch (the taskbar's
/// pinned apps), a button per window that groups by program when the taskbar fills, and on the primary monitor the
/// notification area with its chevron and the clock. Drawn as one picture from XP's measured art, with icons and
/// invisible buttons over it.
/// </summary>
internal sealed class XpTaskbarWindow : Window, ITaskbarView
{
    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly TaskbarDock _dock;
    private readonly TaskButtonList _tasks;
    private readonly Grid _root = new();
    private readonly Image _surface = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _overlay = new();
    private readonly XpToolTip _toolTip = new();
    private readonly NotificationArea? _tray;
    private readonly TrayIconInput? _trayInput;
    private readonly Indicators? _indicators;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _collapseTimer;
    // Hit areas and icons by what they are ("start", "task:…", "tray:…"), kept across layouts so a press in progress survives.
    private readonly Dictionary<string, Button> _hits = [];
    private readonly Dictionary<string, Image> _icons = [];
    private readonly Dictionary<nint, (ImageSource? Source, ImageSource? From)> _windowIcons = [];
    private WriteableBitmap? _bitmap;
    private XpMenu? _menu;
    private bool _startOpen;
    private bool _trayExpanded;
    private string? _hot;
    private string? _pressed;
    private int _width;
    // The layout, in the art's pixels.
    private List<(PinnedApp App, int X)> _quickLaunch = [];
    private int? _quickLaunchChevron;
    private List<(TaskButton Button, int X, int Width)> _taskSlots = [];
    private int _trayX;
    private List<(string Key, int X)> _trayItems = [];
    private string _clock = "";
    private int _clockX;
    private bool _chevron;

    public XpTaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings)
    {
        _owner = owner;
        _monitor = monitor;
        _root.Children.Add(_surface);
        _root.Children.Add(_overlay);
        _root.Background = new SolidColorBrush(Color.FromArgb(255, 0x24, 0x5D, 0xDB));
        Content = _root;
        _dock = new TaskbarDock(this, owner, monitor, settings, Height, busy: () => _menu is { IsOpen: true }, hiding: CloseMenu);
        _tasks = new TaskButtonList(owner.Tracker);

        _tray = monitor.IsPrimary ? owner.Tray : null;
        _trayInput = _tray is null ? null : new TrayIconInput(_tray);
        _indicators = monitor.IsPrimary ? owner.Indicators : null;
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _clockTimer = dispatcher.CreateTimer();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _collapseTimer = dispatcher.CreateTimer();
        _collapseTimer.IsRepeating = false;
        // XP folds the hidden icons back a few seconds after the pointer leaves the notification area.
        _collapseTimer.Interval = TimeSpan.FromSeconds(4);
        _collapseTimer.Tick += (_, _) =>
        {
            _trayExpanded = false;
            Refresh();
        };

        _root.SizeChanged += (_, _) => Refresh();
        _root.ContextRequested += Root_ContextRequested;
        _root.KeyDown += Root_KeyDown;
        _root.PointerExited += (_, _) => SetHot(null);
        // A press on the taskbar's empty part closes Start (the taskbar doesn't take the focus, so Start keeps it).
        _root.PointerPressed += (_, _) => _owner.HideStartMenu();
        if (_tray is not null)
        {
            _tray.PromotedIcons.CollectionChanged += OnTrayChanged;
            _tray.OverflowIcons.CollectionChanged += OnTrayChanged;
            _tray.ChevronVisibilityChanged += Refresh;
            foreach (TrayIcon icon in _tray.PromotedIcons.Concat(_tray.OverflowIcons))
                icon.PropertyChanged += OnTrayIconChanged;
        }
        if (_indicators is not null)
            _indicators.Changed += OnIndicatorsChanged;

        Closed += (_, _) =>
        {
            if (_tray is not null)
            {
                _tray.PromotedIcons.CollectionChanged -= OnTrayChanged;
                _tray.OverflowIcons.CollectionChanged -= OnTrayChanged;
                _tray.ChevronVisibilityChanged -= Refresh;
                foreach (TrayIcon icon in _tray.PromotedIcons.Concat(_tray.OverflowIcons))
                    icon.PropertyChanged -= OnTrayIconChanged;
            }
            if (_indicators is not null)
                _indicators.Changed -= OnIndicatorsChanged;
            _trayInput?.Stop();
            _clockTimer.Stop();
            _collapseTimer.Stop();
            _toolTip.Shut();
        };
        UpdateClock();
    }

    public DisplayMonitor Monitor => _monitor;

    public nint Handle => _dock.Handle;

    public RectInt32 ScreenBounds => _dock.ScreenBounds;

    private double Scale => _monitor.Dpi / 96.0;

    public void Show() => AppWindow.Show(activateWindow: false);

    public void Shut()
    {
        CloseMenu();
        _toolTip.Cancel();
        _dock.Shut();
        Close();
    }

    public void Reveal() => _dock.Reveal();

    /// <summary>Start opened or closed above this taskbar: the Start button stays pressed while it's open.</summary>
    public void ShowStartOpen(bool open)
    {
        _startOpen = open;
        Paint();
    }

    public void RefreshTasks() => Refresh();

    public void RefreshTray() => Refresh();

    // XP's colours are its own: Windows' light or dark mode, the accent colour and the backdrop don't change them.
    public void SetTheme(ElementTheme theme, Color? accent) { }

    public void SetBackdrop(Backdrop kind) { }

    public void UpdateClock()
    {
        DateTime now = DateTime.Now;
        string clock = RegionalTime.Format(now, seconds: false);
        if (clock != _clock)
        {
            _clock = clock;
            Refresh();
        }
        // Next at the turn of the minute.
        _clockTimer.Interval = TimeSpan.FromMilliseconds(60_000 - now.Second * 1000 - now.Millisecond + 50);
        _clockTimer.Start();
    }

    // XP had no notification center, search box, Quick Settings, input switcher or Quick Link menu.
    public void ShowClockOpen(bool open) { }

    public void SetSearchMode(TaskbarSearchMode mode) { }

    public void ShowQuickSettings(QuickSettingsPage page) { }

    public void RunInputSwitch(InputSwitchCommand command) { }

    public void ToggleQuickLinks() { }

    public void ShowJumpList(int index) { }

    public void SetFullScreenWindow(nint window) => _dock.SetFullScreenWindow(window);

    public RectInt32? TaskButtonBounds(nint hwnd)
    {
        TaskButton? button = _tasks.Of(hwnd);
        foreach ((TaskButton slotButton, int x, int width) in _taskSlots)
        {
            if (slotButton == button)
                return ToScreen(x, ButtonTop, width, ButtonHeight);
        }
        return null;
    }

    public RectInt32? TrayIconBounds(TrayIcon icon)
    {
        foreach ((string key, int x) in _trayItems)
        {
            if (key == "tray:" + icon.Key)
                return ToScreen(x, TrayIconTop, 16, 16);
        }
        return _chevron ? ToScreen(_trayX - 10, 4, 21, 22) : null;
    }

    public void ActivateTask(int index)
    {
        if (_tasks.At(index) is not { } button)
            return;
        Reveal();
        if (button.Windows.Count <= 1)
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

    /// <summary>Win+T: the keyboard on the first (or last) task button, or Start when there are none.</summary>
    public void FocusTaskList(bool last = false)
    {
        _dock.TakeKeyboard();
        string key = _taskSlots.Count == 0 ? "start" : "task:" + (last ? _taskSlots[^1] : _taskSlots[0]).Button.Key;
        if (_hits.TryGetValue(key, out Button? hit))
            hit.Focus(FocusState.Keyboard);
    }

    /// <summary>Win+B: the keyboard on the chevron, or the first tray icon.</summary>
    public void FocusTray()
    {
        _dock.TakeKeyboard();
        string? key = _chevron ? "chevron" : _trayItems.FirstOrDefault().Key;
        if (key is not null && _hits.TryGetValue(key, out Button? hit))
            hit.Focus(FocusState.Keyboard);
    }

    private void OnTrayChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (TrayIcon icon in e.OldItems?.OfType<TrayIcon>() ?? [])
            icon.PropertyChanged -= OnTrayIconChanged;
        foreach (TrayIcon icon in e.NewItems?.OfType<TrayIcon>() ?? [])
            icon.PropertyChanged += OnTrayIconChanged;
        Refresh();
    }

    private void OnTrayIconChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void OnIndicatorsChanged() => DispatcherQueue.TryEnqueue(Refresh);

    private RectInt32 ToScreen(int x, int y, int width, int height)
    {
        RectInt32 bar = _dock.ScreenBounds;
        return new RectInt32(bar.X + (int)Math.Round(x * Scale), bar.Y + (int)Math.Round(y * Scale),
            (int)Math.Round(width * Scale), (int)Math.Round(height * Scale));
    }

    /// <summary>Lays the taskbar out again from the pinned apps, the windows and the tray, and draws it.</summary>
    private void Refresh()
    {
        _width = (int)Math.Round(_root.ActualWidth);
        if (_width <= 0)
            return;

        ShellSettings settings = _owner.Settings.Current;
        // Quick Launch: the taskbar's pinned apps, three of them and a chevron for the rest.
        IReadOnlyList<PinnedApp> pinned = settings.PinnedTaskbarApps;
        _quickLaunch = [.. pinned.Take(QuickLaunchVisible).Select((app, i) => (app, BandLeft + i * QuickLaunchButton))];
        _quickLaunchChevron = pinned.Count > QuickLaunchVisible ? BandLeft + QuickLaunchVisible * QuickLaunchButton : null;

        // The notification area, from the right.
        List<string> trayKeys = [];
        _chevron = _tray is not null && _tray.ChevronVisible && _tray.OverflowIcons.Count > 0;
        if (_tray is not null)
        {
            IEnumerable<TrayIcon> shown = _trayExpanded && _chevron ? _tray.OverflowIcons.Concat(_tray.PromotedIcons) : _tray.PromotedIcons;
            trayKeys.AddRange(shown.Select(icon => "tray:" + icon.Key));
        }
        if (_indicators is not null)
            trayKeys.AddRange(["indicator:network", "indicator:volume"]);
        int clockWidth = Interop.Imaging.GdiText.Measure(_clock, XpArt.Tahoma);
        int trayWidth = _monitor.IsPrimary ? TrayWidth(trayKeys.Count, clockWidth) : 0;
        _trayX = _width - trayWidth;
        _trayItems = [.. trayKeys.Select((key, i) => (key, _trayX + TrayIconsLeft + i * TrayIconSpacing))];
        _clockX = _width - ClockRight - clockWidth;

        // The task buttons between Quick Launch and the notification area.
        int bandLeft = BandLeft + QuickLaunchWidth(pinned.Count);
        int bandRight = (_monitor.IsPrimary ? _trayX : _width) - BandGap;
        IReadOnlyList<TaskButtonModel> models = Group(_owner.Tracker.Windows, bandRight - bandLeft, settings.CombineButtons != CombineButtons.Never);
        _owner.TaskOrder = [.. models.Select(model => model.Key)];
        _tasks.Update(models, combine: false);
        int[] widths = ButtonWidths(_tasks.Count, bandRight - bandLeft);
        _taskSlots = [];
        int x = bandLeft;
        for (int i = 0; i < _tasks.Count; i++)
        {
            _taskSlots.Add((_tasks[i], x, widths[i]));
            x += widths[i];
        }

        PlaceElements();
        Paint();
    }

    // The invisible buttons and the icons over the picture.
    private void PlaceElements()
    {
        var keys = new HashSet<string>();
        Button Hit(string key, string automationId, string name, int x, int y, int width, int height)
        {
            keys.Add(key);
            if (!_hits.TryGetValue(key, out Button? hit))
            {
                _hits[key] = hit = XpHit.Create(automationId, name);
                Wire(hit, key);
                _overlay.Children.Add(hit);
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hit, name);
            XpHit.Place(hit, x, y, width, height);
            return hit;
        }
        var iconKeys = new HashSet<string>();
        void Icon(string key, ImageSource? source, int x, int y)
        {
            iconKeys.Add(key);
            if (!_icons.TryGetValue(key, out Image? image))
            {
                _icons[key] = image = new Image { IsHitTestVisible = false };
                _overlay.Children.Insert(0, image);
            }
            image.Source = source;
            XpHit.Place(image, x, y, 16, 16);
        }

        Hit("start", "StartButton", "start", 0, 0, StartWidth, Height).IsTabStop = true;
        for (int i = 0; i < _quickLaunch.Count; i++)
        {
            (PinnedApp app, int x) = _quickLaunch[i];
            Hit($"ql:{i}", "QuickLaunchButton", app.DisplayName, x, ButtonTop, QuickLaunchButton, ButtonHeight);
            Icon($"ql:{i}", Pending($"ql:{i}", XpIcons.Of(app, 16)), x + 3 + Shift($"ql:{i}"), 8 + Shift($"ql:{i}"));
        }
        if (_quickLaunchChevron is { } chevronX)
            Hit("qlchevron", "QuickLaunchChevron", "More Quick Launch items", chevronX, ButtonTop, QuickLaunchButton, ButtonHeight);
        foreach ((TaskButton button, int x, int width) in _taskSlots)
        {
            string key = "task:" + button.Key;
            Hit(key, "TaskButton", button.Windows.Count > 1 ? $"{button.Windows.Count} {button.Title}" : button.Title, x, ButtonTop, width, ButtonHeight).IsTabStop = true;
            int shift = button.IsActive || _pressed == key ? 1 : 0;
            Icon(key, SmallIcon(button), x + IconLeft + shift, ButtonTop + IconTop + shift);
        }
        if (_chevron)
            Hit("chevron", "TrayChevron", _trayExpanded ? "Hide" : "Show hidden icons", _trayX - 10, 6, 21, 19);
        foreach ((string key, int x) in _trayItems)
        {
            Hit(key, key.StartsWith("indicator:", StringComparison.Ordinal) ? key == "indicator:volume" ? "VolumeIndicator" : "NetworkIndicator" : "TrayIcon",
                TrayName(key), x - 1, 4, 18, 22);
            Icon(key, TrayIconSource(key), x, TrayIconTop);
        }
        if (_monitor.IsPrimary)
            Hit("clock", "ClockButton", _clock, _clockX - 4, 4, _width - _clockX - ClockRight + 8, 22);

        foreach (string stale in _hits.Keys.Where(key => !keys.Contains(key)).ToList())
        {
            _overlay.Children.Remove(_hits[stale]);
            _hits.Remove(stale);
        }
        foreach (string stale in _icons.Keys.Where(key => !iconKeys.Contains(key)).ToList())
        {
            _overlay.Children.Remove(_icons[stale]);
            _icons.Remove(stale);
        }
    }

    private int Shift(string key) => _pressed == key ? 1 : 0;

    // An icon that's still loading shows once it's there.
    private ImageSource? Pending(string key, Task<ImageSource?> icon)
    {
        if (icon.IsCompletedSuccessfully)
            return icon.Result;
        icon.ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (_icons.TryGetValue(key, out Image? image))
                image.Source = icon.Result;
        }), TaskScheduler.Default);
        return null;
    }

    // XP's task buttons show the window's small icon; the tracker keeps its large one, which says when it changed.
    private ImageSource? SmallIcon(TaskButton button)
    {
        if (button.Windows.Count == 0)
            return button.Icon;
        nint hwnd = button.Windows[0].Handle;
        if (_windowIcons.TryGetValue(hwnd, out (ImageSource? Source, ImageSource? From) known) && known.From == button.Icon)
            return known.Source ?? button.Icon;

        _windowIcons[hwnd] = (null, button.Icon);
        ImageSource? from = button.Icon;
        _ = LoadSmallIcon(hwnd, from);
        return button.Icon;
    }

    private async Task LoadSmallIcon(nint hwnd, ImageSource? from)
    {
        ImageSource? icon = await XpIcons.OfWindow(hwnd);
        if (_windowIcons.TryGetValue(hwnd, out (ImageSource? Source, ImageSource? From) known) && known.From == from && icon is not null)
        {
            _windowIcons[hwnd] = (icon, from);
            PlaceElements();
        }
    }

    private string TrayName(string key) => key switch
    {
        "indicator:volume" when _indicators is not null => IndicatorDisplay.VolumeToolTip(_indicators.AudioDeviceName, _indicators.Volume, _indicators.IsMuted),
        "indicator:network" when _indicators is not null => IndicatorDisplay.NetworkToolTip(_indicators.Network, _indicators.AirplaneMode == true),
        _ => TrayIconOf(key)?.Name ?? "",
    };

    private ImageSource? TrayIconSource(string key) => key switch
    {
        // XP's own pictures, still in Windows: the grey speaker, and the two screens (a warning when there's no internet).
        "indicator:volume" => Pending(key, XpIcons.FromResource(@"%SystemRoot%\System32\SndVolSSO.dll,0", 16)),
        "indicator:network" => Pending(key, XpIcons.FromResource(
            _indicators?.Network.HasInternet == true ? @"%SystemRoot%\System32\netshell.dll,1" : @"%SystemRoot%\System32\netshell.dll,4", 16)),
        _ => TrayIconOf(key)?.Icon,
    };

    private TrayIcon? TrayIconOf(string key) =>
        _tray?.PromotedIcons.Concat(_tray.OverflowIcons).FirstOrDefault(icon => "tray:" + icon.Key == key);

    private void Paint()
    {
        if (_width <= 0)
            return;

        var canvas = new XpCanvas(_width, Height);
        XpButtonState State(string key, bool pressedAlready = false)
        {
            bool pressed = pressedAlready || _pressed == key;
            bool hot = _hot == key;
            return (pressed, hot) switch
            {
                (true, true) => XpButtonState.PressedHot,
                (true, false) => XpButtonState.Pressed,
                (false, true) => XpButtonState.Hot,
                _ => XpButtonState.Normal,
            };
        }
        var scene = new XpTaskbarScene(
            _width,
            _startOpen || _pressed == "start" ? XpButtonState.Pressed : _hot == "start" ? XpButtonState.Hot : XpButtonState.Normal,
            [.. _quickLaunch.Select((item, i) => (item.X, State($"ql:{i}")))],
            _quickLaunchChevron is { } chevronX ? (chevronX, State("qlchevron", _menu is { IsOpen: true } && _menuFor == "qlchevron")) : null,
            [.. _taskSlots.Select(slot => new XpTaskSlot(slot.X, slot.Width, slot.Button.Title,
                slot.Button.Windows.Count > 1 ? slot.Button.Windows.Count : null,
                State("task:" + slot.Button.Key, slot.Button.IsActive || _menu is { IsOpen: true } && _menuFor == "task:" + slot.Button.Key),
                slot.Button.IsFlashing))],
            _monitor.IsPrimary ? new XpTraySlot(_trayX, _chevron ? _trayExpanded : null, _hot == "chevron", _clock, _clockX) : null);
        XpTaskbarPainter.Paint(canvas, scene);

        if (_bitmap is null || _bitmap.PixelWidth != canvas.Width)
        {
            _bitmap = new WriteableBitmap(canvas.Width, canvas.Height);
            _surface.Source = _bitmap;
            _surface.Width = _overlay.Width = canvas.Width;
            _surface.Height = _overlay.Height = canvas.Height;
        }
        using (Stream stream = _bitmap.PixelBuffer.AsStream())
            stream.Write(MemoryMarshal.AsBytes(canvas.Pixels.AsSpan()));
        _bitmap.Invalidate();
    }

    // What the open menu belongs to, which shows pressed while it's open.
    private string? _menuFor;

    private void SetHot(string? key)
    {
        if (_hot == key)
            return;
        _hot = key;
        Paint();
    }

    private void Wire(Button hit, string key)
    {
        hit.PointerEntered += (_, e) =>
        {
            SetHot(key);
            if (TrayIconOf(key) is { } icon)
                _trayInput?.Enter(icon);
            if (key.StartsWith("tray:", StringComparison.Ordinal) || key.StartsWith("indicator:", StringComparison.Ordinal) || key == "chevron")
                _collapseTimer.Stop();
            RequestToolTip(key);
        };
        hit.PointerExited += (_, _) =>
        {
            if (_hot == key)
                SetHot(null);
            _toolTip.Cancel();
            if (TrayIconOf(key) is not null)
                _trayInput?.EndHover();
            if (_trayExpanded)
                _collapseTimer.Start();
        };
        hit.GotFocus += (_, _) => SetHot(key);
        hit.LostFocus += (_, _) =>
        {
            if (_hot == key)
                SetHot(null);
        };
        hit.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => Pressed(key, hit, e)), handledEventsToo: true);
        hit.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) => Released(key, hit, e)), handledEventsToo: true);
        hit.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (TrayIconOf(key) is { } icon)
                _trayInput?.Move(icon);
        }), handledEventsToo: true);
        hit.PointerCaptureLost += (_, _) =>
        {
            if (_pressed == key)
            {
                _pressed = null;
                Paint();
            }
        };
        hit.Click += (_, _) => Clicked(key);
        hit.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ContextMenu(key, e.TryGetPosition(_root, out Windows.Foundation.Point point) ? point : null);
        };
        hit.DoubleTapped += (_, _) =>
        {
            if (key == "clock")
                Launcher.OpenSettings(_owner.RunMode, "Date and time settings", "ms-settings:dateandtime", "timedate.cpl");
            else if (key == "indicator:volume")
                Launcher.Launch(new PinnedApp("Volume Mixer", Path: "sndvol.exe"));
        };
    }

    private void Pressed(string key, Button hit, PointerRoutedEventArgs e)
    {
        _toolTip.Cancel();
        PointerPointProperties properties = e.GetCurrentPoint(hit).Properties;
        if (TrayIconOf(key) is { } icon)
        {
            if (_trayInput!.PressEvent(icon, properties) is { } mouseEvent)
                _trayInput.Press(icon, mouseEvent);
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;

        // Start closes on a press anywhere else on the taskbar, as XP's did.
        if (key != "start")
            _owner.HideStartMenu();
        if (key != "clock" && !key.StartsWith("indicator:", StringComparison.Ordinal))
        {
            _pressed = key;
            PlaceElements();
            Paint();
        }

    }

    private void Released(string key, Button hit, PointerRoutedEventArgs e)
    {
        if (TrayIconOf(key) is { } icon && TrayIconInput.ReleaseEvent(e.GetCurrentPoint(hit).Properties.PointerUpdateKind) is { } released)
            _trayInput!.Release(icon, released);
        if (_pressed == key)
        {
            _pressed = null;
            PlaceElements();
            Paint();
        }
    }

    private void Clicked(string key)
    {
        if (key == "start")
        {
            _owner.ToggleStartMenu(this);
        }
        else if (key.StartsWith("ql:", StringComparison.Ordinal) && int.TryParse(key[3..], out int index) && index < _quickLaunch.Count)
        {
            Launcher.Launch(_quickLaunch[index].App);
        }
        else if (key == "qlchevron")
        {
            ShowMenu(key, [.. _owner.Settings.Current.PinnedTaskbarApps.Skip(QuickLaunchVisible).Select(app => new XpMenuItem
            {
                Text = app.DisplayName,
                Icon = () => XpIcons.Of(app, 16),
                Invoke = () => Launcher.Launch(app),
                AutomationId = "QuickLaunchMenuItem",
            })], XpMenuStyle.Classic, ToScreen(_quickLaunchChevron!.Value + 4, ButtonTop, 17, 0), XpMenuPlacement.Above);
        }
        else if (key.StartsWith("task:", StringComparison.Ordinal) && TaskOf(key) is { } button)
        {
            if (TaskButtonList.ClickActs(button))
                _tasks.Click(button);
            else
                ShowGroupMenu(key, button);
        }
        else if (key == "chevron")
        {
            _trayExpanded = !_trayExpanded;
            Refresh();
            if (_trayExpanded)
                _collapseTimer.Start();
        }
        else if (key == "indicator:network")
        {
            Launcher.OpenSettings(_owner.RunMode, "Network Connections", "ms-settings:network", "ncpa.cpl");
        }
        else if (key == "indicator:volume")
        {
            // XP's speaker opened a small volume slider; Windows' classic one is SndVol's -f, at the icon.
            RectInt32 at = ToScreen(_trayItems.First(item => item.Key == key).X, 0, 16, 0);
            Launcher.Launch(new PinnedApp("Volume", Path: "sndvol.exe", Arguments: $"-f {(at.Y << 16) | (at.X & 0xFFFF)}"));
        }
    }

    private TaskButton? TaskOf(string key) => _taskSlots.Select(slot => slot.Button).FirstOrDefault(button => "task:" + button.Key == key);

    private void ShowGroupMenu(string key, TaskButton button)
    {
        (_, int x, int width) = _taskSlots.First(slot => slot.Button == button);
        ShowMenu(key, [.. button.Windows.Select(window => new XpMenuItem
        {
            Text = GroupItemTitle(window.Title, button.App.DisplayName),
            Icon = () => XpIcons.OfWindow(window.Handle),
            Invoke = () => TopLevelWindows.Activate(window.Handle),
            AutomationId = "GroupMenuItem",
        })], XpMenuStyle.Group, ToScreen(x, 0, width, 0), XpMenuPlacement.Above, width + 9);
    }

    private void ShowMenu(string key, IReadOnlyList<XpMenuItem> items, XpMenuStyle style, RectInt32 anchor, XpMenuPlacement placement, int minWidth = 0)
    {
        CloseMenu();
        if (items.Count == 0)
            return;
        _toolTip.Cancel();
        _menuFor = key;
        _menu = XpMenu.Show(items, style, _monitor, anchor, placement, () =>
        {
            _menuFor = null;
            Paint();
        }, minWidth: minWidth);
        Paint();
    }

    private void CloseMenu()
    {
        _menu?.CloseAll();
        _menu = null;
        _menuFor = null;
    }

    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        ContextMenu(null, e.TryGetPosition(_root, out Windows.Foundation.Point point) ? point : null);
    }

    // A right-click: a task button's system menu, a group's menu, nothing on a tray icon (its app shows its own), and the
    // taskbar's menu elsewhere.
    private void ContextMenu(string? key, Windows.Foundation.Point? at)
    {
        if (key is not null && (key.StartsWith("tray:", StringComparison.Ordinal)))
            return;

        PointInt32 pointer = Cursor.Position();
        var anchor = new RectInt32(pointer.X, pointer.Y - 1, 0, 0);
        if (key is not null && key.StartsWith("task:", StringComparison.Ordinal) && TaskOf(key) is { } button)
        {
            if (button.Windows.Count == 1)
                ShowMenu(key, SystemMenuItems(button.Windows[0].Handle), XpMenuStyle.Classic, anchor, XpMenuPlacement.AtPoint);
            else if (button.Windows.Count > 1)
                ShowMenu(key, GroupCommands(button), XpMenuStyle.Classic, anchor, XpMenuPlacement.AtPoint);
            return;
        }
        ShowMenu("taskbar", TaskbarMenu(key == "clock" || key is not null && key.StartsWith("indicator:", StringComparison.Ordinal)),
            XpMenuStyle.Classic, anchor, XpMenuPlacement.AtPoint);
    }

    private static IReadOnlyList<XpMenuItem> SystemMenuItems(nint hwnd)
    {
        static XpMenuItem Item(SystemMenuItem item, nint hwnd) => item.IsSeparator ? XpMenuItem.Separator : new XpMenuItem
        {
            Text = item.Text,
            Shortcut = item.Shortcut,
            IsEnabled = item.IsEnabled,
            IsDefault = item.IsDefault,
            Glyph = item.Glyph,
            Invoke = () => SystemMenu.Invoke(hwnd, item.Command),
            Submenu = item.Items.Count > 0 ? () => [.. item.Items.Select(sub => Item(sub, hwnd))] : null,
            AutomationId = "SystemMenuItem",
        };
        return [.. SystemMenu.Read(hwnd).Select(item => Item(item, hwnd))];
    }

    private static IReadOnlyList<XpMenuItem> GroupCommands(TaskButton button)
    {
        IReadOnlyList<nint> windows = [.. button.Windows.Select(window => window.Handle)];
        return
        [
            new() { Text = "Cascade", Invoke = () => TopLevelWindows.Cascade(windows), AutomationId = "CascadeGroupMenuItem" },
            new() { Text = "Tile Horizontally", Invoke = () => TopLevelWindows.Tile(windows, sideBySide: false), AutomationId = "TileGroupHorizontallyMenuItem" },
            new() { Text = "Tile Vertically", Invoke = () => TopLevelWindows.Tile(windows, sideBySide: true), AutomationId = "TileGroupVerticallyMenuItem" },
            new() { Text = "Minimize Group", Invoke = () => { foreach (nint hwnd in windows) TopLevelWindows.Minimize(hwnd); }, AutomationId = "MinimizeGroupMenuItem" },
            XpMenuItem.Separator,
            new() { Text = "Close Group", Invoke = () => { foreach (nint hwnd in windows) TopLevelWindows.Close(hwnd); }, AutomationId = "CloseGroupMenuItem" },
        ];
    }

    // XP's taskbar menu, without its toolbars (Quick Launch is the pinned apps) and with Properties opening NeoShell's
    // taskbar settings; alongside Explorer, Exit NeoShell as on Windows 11's.
    private IReadOnlyList<XpMenuItem> TaskbarMenu(bool fromTray)
    {
        var items = new List<XpMenuItem>();
        if (fromTray)
        {
            items.Add(new() { Text = "Adjust Date/Time", Invoke = () => Launcher.OpenSettings(_owner.RunMode, "Date and time settings", "ms-settings:dateandtime", "timedate.cpl"), AutomationId = "AdjustDateTimeMenuItem" });
            items.Add(new() { Text = "Customize Notifications...", Invoke = () => _owner.OpenSettings(SettingsPage.Taskbar), AutomationId = "CustomizeNotificationsMenuItem" });
            items.Add(XpMenuItem.Separator);
        }
        items.AddRange(
        [
            new() { Text = "Cascade Windows", Invoke = () => TopLevelWindows.Cascade([]), AutomationId = "CascadeWindowsMenuItem" },
            new() { Text = "Tile Windows Horizontally", Invoke = () => TopLevelWindows.Tile([], sideBySide: false), AutomationId = "TileHorizontallyMenuItem" },
            new() { Text = "Tile Windows Vertically", Invoke = () => TopLevelWindows.Tile([], sideBySide: true), AutomationId = "TileVerticallyMenuItem" },
            new() { Text = "Show the Desktop", Invoke = _owner.ToggleDesktop, AutomationId = "ShowDesktopMenuItem" },
            XpMenuItem.Separator,
            new() { Text = "Task Manager", Invoke = _owner.OpenTaskManager, AutomationId = "TaskManagerMenuItem" },
            XpMenuItem.Separator,
            // NeoShell's taskbar is always locked: the item shows as XP's did and does nothing.
            new() { Text = "Lock the Taskbar", IsChecked = true, AutomationId = "LockTaskbarMenuItem" },
            new() { Text = "Properties", Invoke = () => _owner.OpenSettings(SettingsPage.Taskbar), AutomationId = "TaskbarPropertiesMenuItem" },
        ]);
        if (_owner.RunMode == RunMode.AlongsideExplorer)
        {
            items.Add(XpMenuItem.Separator);
            items.Add(new() { Text = "Exit NeoShell", Invoke = _owner.Exit, AutomationId = "ExitMenuItem" });
        }
        return items;
    }

    private void RequestToolTip(string key)
    {
        string? text = key switch
        {
            "start" => "Click here to begin",
            "qlchevron" => null,
            "chevron" => _trayExpanded ? "Hide" : "Show hidden icons",
            "clock" => DateTime.Now.ToString("D", CultureInfo.CurrentCulture),
            _ when key.StartsWith("ql:", StringComparison.Ordinal) && int.TryParse(key[3..], out int i) && i < _quickLaunch.Count => _quickLaunch[i].App.DisplayName,
            // A task button's title, when its label is cut off.
            _ when key.StartsWith("task:", StringComparison.Ordinal) => TruncatedTitle(key),
            _ when key.StartsWith("indicator:", StringComparison.Ordinal) => TrayName(key),
            _ => TrayIconOf(key)?.ToolTip,
        };
        if (text is null)
            return;
        _toolTip.Request(text, () => (new PointInt32(Cursor.Position().X, _dock.ScreenBounds.Y), true, _monitor));
    }

    private string? TruncatedTitle(string key)
    {
        foreach ((TaskButton button, _, int width) in _taskSlots)
        {
            if ("task:" + button.Key == key && button.Windows.Count == 1
                && Interop.Imaging.GdiText.Measure(button.Title, XpArt.Tahoma) > width - LabelLeft - LabelRight - 1)
            {
                return button.Title;
            }
        }
        return null;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_menu is { IsOpen: true } menu && menu.HandleKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        // Space and Enter press the focused button (the Button does); Escape leaves the taskbar.
        if (e.Key == VirtualKey.Escape)
        {
            TopLevelWindows.Activate(_owner.Tracker.Foreground);
            e.Handled = true;
        }
    }
}
