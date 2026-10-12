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
using static NeoShell.Themes.Windows98.Win98TaskbarLayout;

namespace NeoShell.Themes.Windows98;

/// <summary>
/// Windows 98's taskbar on one monitor (see docs/design/theme-98.md): the Start button, Quick Launch (the taskbar's
/// pinned apps), a button per window, and on the primary monitor the notification area with the clock. Drawn as one
/// picture from 98's measured numbers, with icons and invisible buttons over it, as the XP theme's.
/// </summary>
internal sealed class Win98TaskbarWindow : Window, ITaskbarView
{
    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly TaskbarDock _dock;
    private readonly TaskButtonList _tasks;
    private readonly Grid _root = new();
    private readonly Image _surface = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _overlay = new();
    private readonly NotificationArea? _tray;
    private readonly TrayIconInput? _trayInput;
    private readonly DispatcherQueueTimer _clockTimer;
    // Hit areas and icons by what they are ("start", "ql:…", "task:…", "tray:…"), kept across layouts so a press survives.
    private readonly Dictionary<string, Button> _hits = [];
    private readonly Dictionary<string, Image> _icons = [];
    private WriteableBitmap? _bitmap;
    private bool _startOpen;
    // 98's Start opens on the press; the button's click that follows mustn't toggle it again.
    private bool _startOpenedByPress;
    private string? _hot;
    private string? _pressed;
    private int _width;
    // The layout, in the art's pixels.
    private List<(PinnedApp App, int X)> _quickLaunch = [];
    private List<(TaskButton Button, int X, int Width)> _taskSlots = [];
    private int? _trayX;
    private List<(string Key, int X)> _trayItems = [];
    private string _clock = "";
    private int _clockX;

    public Win98TaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings)
    {
        _owner = owner;
        _monitor = monitor;
        _root.Children.Add(_surface);
        _root.Children.Add(_overlay);
        _root.Background = new SolidColorBrush(Color.FromArgb(255, 0xC0, 0xC0, 0xC0));
        Content = _root;
        _dock = new TaskbarDock(this, owner, monitor, settings, Height, busy: () => false, hiding: () => { });
        _tasks = new TaskButtonList(owner.Tracker);

        _tray = monitor.IsPrimary ? owner.Tray : null;
        _trayInput = _tray is null ? null : new TrayIconInput(_tray);
        _clockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clockTimer.Tick += (_, _) => UpdateClock();

        _root.SizeChanged += (_, _) => Refresh();
        _root.KeyDown += Root_KeyDown;
        _root.PointerExited += (_, _) => SetHot(null);
        // A press on the taskbar's empty part closes Start (the taskbar doesn't take the focus, so Start keeps it).
        _root.PointerPressed += (_, _) => _owner.HideStartMenu();
        if (_tray is not null)
        {
            _tray.PromotedIcons.CollectionChanged += OnTrayChanged;
            _tray.OverflowIcons.CollectionChanged += OnTrayChanged;
            foreach (TrayIcon icon in _tray.PromotedIcons.Concat(_tray.OverflowIcons))
                icon.PropertyChanged += OnTrayIconChanged;
        }

        Closed += (_, _) =>
        {
            if (_tray is not null)
            {
                _tray.PromotedIcons.CollectionChanged -= OnTrayChanged;
                _tray.OverflowIcons.CollectionChanged -= OnTrayChanged;
                foreach (TrayIcon icon in _tray.PromotedIcons.Concat(_tray.OverflowIcons))
                    icon.PropertyChanged -= OnTrayIconChanged;
            }
            _trayInput?.Stop();
            _clockTimer.Stop();
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
        _dock.Shut();
        Close();
    }

    public void Reveal() => _dock.Reveal();

    /// <summary>Start opened or closed above this taskbar: the Start button stays pushed while it's open.</summary>
    public void ShowStartOpen(bool open)
    {
        _startOpen = open;
        Paint();
    }

    public void RefreshTasks() => Refresh();

    public void RefreshTray() => Refresh();

    // 98's colours are its scheme's: Windows' light or dark mode, the accent colour and the backdrop don't change them.
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

    // 98 had no notification center, search box, Quick Settings, input switcher, Quick Link menu or jump lists.
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
                return ToScreen(x, ButtonTop + TrayIconTop, 16, 16);
        }
        return null;
    }

    public void ActivateTask(int index)
    {
        if (_tasks.At(index) is not { } button)
            return;
        Reveal();
        _tasks.Click(button);
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

    /// <summary>Win+B: the keyboard on the first tray icon.</summary>
    public void FocusTray()
    {
        _dock.TakeKeyboard();
        if (_trayItems.Count > 0 && _hits.TryGetValue(_trayItems[0].Key, out Button? hit))
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

        // Quick Launch: the taskbar's pinned apps.
        IReadOnlyList<PinnedApp> pinned = _owner.Settings.Current.PinnedTaskbarApps;
        int quickLaunchLeft = FirstBand + ContentLeft;
        _quickLaunch = [.. pinned.Take(QuickLaunchVisible).Select((app, i) => (app, quickLaunchLeft + i * QuickLaunchButton))];

        // The notification area, from the right: every icon shown (98 hid none).
        List<string> trayKeys = _tray is null ? [] : [.. _tray.OverflowIcons.Concat(_tray.PromotedIcons).Select(icon => "tray:" + icon.Key)];
        int clockWidth = Interop.Imaging.GdiText.Measure(_clock, Win98Art.Font);
        _trayX = _monitor.IsPrimary ? _width - TrayRight - TrayWidth(trayKeys.Count, clockWidth) : null;
        _trayItems = _trayX is { } trayX ? [.. trayKeys.Select((key, i) => (key, trayX + TrayIconsLeft + i * TrayIconSpacing))] : [];
        _clockX = _width - TrayRight - ClockRight - clockWidth;

        // The task buttons between Quick Launch and the notification area, one per window in the order they appeared.
        int bandLeft = TaskButtonsLeft(_quickLaunch.Count);
        int bandRight = (_trayX is { } x ? x - TrayEtch : _width) - BandGap;
        IReadOnlyList<TaskButtonModel> models = [.. _owner.Tracker.Windows.Select(window => new TaskButtonModel($"window:{window.Handle}", null, [window]))];
        _owner.TaskOrder = [.. models.Select(model => model.Key)];
        _tasks.Update(models, combine: false);
        int[] widths = ButtonWidths(_tasks.Count, bandRight - bandLeft);
        _taskSlots = [];
        int left = bandLeft;
        for (int i = 0; i < _tasks.Count; i++)
        {
            _taskSlots.Add((_tasks[i], left, widths[i]));
            left += widths[i] + ButtonGap;
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
                _hits[key] = hit = ThemeHit.Create(automationId, name);
                Wire(hit, key);
                _overlay.Children.Add(hit);
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hit, name);
            ThemeHit.Place(hit, x, y, width, height);
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
            ThemeHit.Place(image, x, y, 16, 16);
        }

        Hit("start", "StartButton", "Start", StartLeft, ButtonTop, StartWidth, ButtonHeight).IsTabStop = true;
        for (int i = 0; i < _quickLaunch.Count; i++)
        {
            (PinnedApp app, int x) = _quickLaunch[i];
            string key = $"ql:{i}";
            Hit(key, "QuickLaunchButton", app.DisplayName, x, ButtonTop, QuickLaunchButton, ButtonHeight);
            int shift = _pressed == key ? 1 : 0;
            Icon(key, Pending(key, ThemeIcons.Of(app, 16)), x + 3 + shift, ButtonTop + 3 + shift);
        }
        foreach ((TaskButton button, int x, int width) in _taskSlots)
        {
            string key = "task:" + button.Key;
            Hit(key, "TaskButton", button.Title, x, ButtonTop, width, ButtonHeight).IsTabStop = true;
            int shift = button.IsActive || _pressed == key ? 1 : 0;
            Icon(key, button.Icon, x + IconLeft + shift, ButtonTop + IconTop + shift);
        }
        foreach ((string key, int x) in _trayItems)
        {
            Hit(key, "TrayIcon", TrayIconOf(key)?.Name ?? "", x, ButtonTop + 2, 16, ButtonHeight - 4);
            Icon(key, TrayIconOf(key)?.Icon, x, ButtonTop + TrayIconTop);
        }
        if (_trayX is not null)
            Hit("clock", "ClockButton", _clock, _clockX - 4, ButtonTop + 2, _width - TrayRight - _clockX, ButtonHeight - 4);

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

    private TrayIcon? TrayIconOf(string key) =>
        _tray?.PromotedIcons.Concat(_tray.OverflowIcons).FirstOrDefault(icon => "tray:" + icon.Key == key);

    private void Paint()
    {
        if (_width <= 0)
            return;

        var canvas = new ThemeCanvas(_width, Height);
        Win98ButtonState State(string key) =>
            _pressed == key ? Win98ButtonState.Pressed : _hot == key ? Win98ButtonState.Hot : Win98ButtonState.Normal;
        var scene = new Win98TaskbarScene(
            _width,
            _startOpen || _pressed == "start",
            [.. _quickLaunch.Select((item, i) => (item.X, State($"ql:{i}")))],
            [.. _taskSlots.Select(slot => new Win98TaskSlot(slot.X, slot.Width, slot.Button.Title, slot.Button.IsActive, _pressed == "task:" + slot.Button.Key))],
            _trayX,
            _clock,
            _clockX);
        Win98TaskbarPainter.Paint(canvas, scene);

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

    private void SetHot(string? key)
    {
        if (_hot == key)
            return;
        _hot = key;
        Paint();
    }

    private void Wire(Button hit, string key)
    {
        hit.PointerEntered += (_, _) =>
        {
            SetHot(key);
            if (TrayIconOf(key) is { } icon)
                _trayInput?.Enter(icon);
        };
        hit.PointerExited += (_, _) =>
        {
            if (_hot == key)
                SetHot(null);
            if (TrayIconOf(key) is not null)
                _trayInput?.EndHover();
        };
        hit.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => Pressed(key, hit, e)), handledEventsToo: true);
        hit.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) => Released(key, hit, e)), handledEventsToo: true);
        hit.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) =>
        {
            if (TrayIconOf(key) is { } icon)
                _trayInput?.Move(icon);
        }), handledEventsToo: true);
        hit.PointerCaptureLost += (_, _) =>
        {
            if (_pressed == key)
            {
                _pressed = null;
                PlaceElements();
                Paint();
            }
        };
        hit.Click += (_, _) => Clicked(key);
        hit.DoubleTapped += (_, _) =>
        {
            if (key == "clock")
                Launcher.OpenSettings(_owner.RunMode, "Date and time settings", "ms-settings:dateandtime", "timedate.cpl");
        };
    }

    private void Pressed(string key, Button hit, PointerRoutedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(hit).Properties;
        if (TrayIconOf(key) is { } icon)
        {
            if (_trayInput!.PressEvent(icon, properties) is { } mouseEvent)
                _trayInput.Press(icon, mouseEvent);
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;

        if (key == "start")
        {
            // After the press is through: the button taking the focus would otherwise take the keyboard back from Start.
            _startOpenedByPress = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _owner.ToggleStartMenu(this));
            return;
        }
        _owner.HideStartMenu();
        if (key != "clock")
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
        if (key == "start")
            DispatcherQueue.TryEnqueue(() => _startOpenedByPress = false);
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
            // The keyboard's Space or Enter; a pointer already toggled Start on its press.
            if (!_startOpenedByPress)
                _owner.ToggleStartMenu(this);
            _startOpenedByPress = false;
        }
        else if (key.StartsWith("ql:", StringComparison.Ordinal) && int.TryParse(key[3..], out int index) && index < _quickLaunch.Count)
        {
            Launcher.Launch(_quickLaunch[index].App);
        }
        else if (key.StartsWith("task:", StringComparison.Ordinal)
            && _taskSlots.Select(slot => slot.Button).FirstOrDefault(button => "task:" + button.Key == key) is { } button)
        {
            _tasks.Click(button);
        }
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
}
