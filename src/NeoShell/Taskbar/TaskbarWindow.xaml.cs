using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using Microsoft.UI.Input;
using NeoShell.Interop.Tray;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Tray;
using Windows.Foundation;
using Windows.Graphics;
using AppBar = NeoShell.Interop.Windowing.AppBar;

namespace NeoShell.Taskbar;

/// <summary>The taskbar on one monitor.</summary>
internal sealed partial class TaskbarWindow : Window
{
    // Effective pixels taken by a Start or Search button, and by the margins around the task list.
    private const double FixedButtonWidth = 44;
    private const double AppsPanelMargins = 2 * 11;

    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly nint _hwnd;
    private readonly AcrylicBackdrop _backdrop = new();
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private readonly AppBar? _appBar;
    private readonly ObservableCollection<TaskButton> _tasks = [];
    private readonly ThumbnailPopup _thumbnails;
    private readonly DispatcherQueueTimer _hoverTimer;
    private readonly DispatcherQueueTimer _hideTimer;
    private (TaskButton Button, FrameworkElement Element)? _hovered;
    private readonly NotificationArea? _tray;
    private readonly Indicators? _indicators;
    private bool _updatingVolumeSlider;
    private readonly DispatcherQueueTimer _trayHoverTimer;
    private TrayIcon? _trayHovered;
    private bool _trayPopupOpen;
    private (TrayIcon Icon, long Time)? _lastTrayLeftDown;
    private nint _fullScreenWindow;
    private readonly bool _autoHide;
    private readonly DispatcherQueueTimer? _autoHideTimer;
    private readonly DispatcherQueueTimer _slideTimer;
    private RectInt32 _shownBounds;
    private RectInt32 _slideFrom;
    private RectInt32 _slideTo;
    private int _slideStep;
    private bool _hidden;
    private bool _isActive;
    private int _pointerAwayTicks;

    public TaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings, ElementTheme theme)
    {
        _owner = owner;
        _monitor = monitor;
        InitializeComponent();

        SystemBackdrop = _backdrop;
        SetTheme(theme);
        AppsPanel.HorizontalAlignment =
            settings.TaskbarAlignment == TaskbarAlignment.Left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        SearchButton.Visibility = settings.ShowSearchButton ? Visibility.Visible : Visibility.Collapsed;
        ExitSeparator.Visibility = ExitItem.Visibility =
            owner.RunMode == RunMode.AlongsideExplorer ? Visibility.Visible : Visibility.Collapsed;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        nint hwnd = _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of Alt+Tab, and clicking it leaves the focus in the app the user is working in.
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        // Win+T makes the taskbar activatable for a while (see FocusTaskList); once it's left, clicks go back to
        // leaving the focus alone.
        Activated += (_, e) =>
        {
            _isActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (!_isActive)
                WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.NoActivate);
        };
        _frameless = new FramelessWindow(hwnd);
        _messages = new WindowSubclass(hwnd, OnMessage);

        _autoHide = settings.AutoHide;
        RectInt32 bounds = TaskbarLayout.Bounds(monitor.Bounds, monitor.Dpi);
        if (_autoHide)
        {
            // Nothing is reserved: windows get the whole screen and the taskbar slides over them. Alongside, it sits
            // above Explorer's taskbar, at the bottom of the work area.
            RectInt32 area = owner.RunMode == RunMode.AlongsideExplorer ? monitor.WorkArea : monitor.Bounds;
            bounds = TaskbarLayout.Bounds(area, monitor.Dpi);
        }
        else if (owner.RunMode == RunMode.AlongsideExplorer)
        {
            // Explorer manages the screen space and puts this bar above its own taskbar.
            _appBar = new AppBar(hwnd);
            bounds = _appBar.DockBottom(monitor.Bounds, bounds.Height);
            _appBar.PositionChanged += () => _placement!.Bounds = _shownBounds = _appBar.DockBottom(monitor.Bounds, bounds.Height);
        }
        else
        {
            WorkArea.Set(TaskbarLayout.WorkArea(monitor.Bounds, bounds));
        }
        _shownBounds = bounds;
        _placement = new PinnedWindow(hwnd, bounds, PinnedLayer.Topmost);

        TaskList.ItemsSource = _tasks;
        // A click anywhere else on the taskbar closes Start, as in Windows; the Start button toggles it itself.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), handledEventsToo: true);
        Root.SizeChanged += (_, _) => RefreshTasks();
        RightPanel.SizeChanged += (_, _) => RefreshTasks();

        _thumbnails = new ThumbnailPopup(owner.Tracker);
        _thumbnails.Root.PointerEntered += (_, _) => _hideTimer!.Stop();
        _thumbnails.Root.PointerExited += (_, _) => _hideTimer!.Start();
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _hoverTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(500), () =>
        {
            if (_hovered is { } hovered && hovered.Button.Windows.Count > 0)
                ShowThumbnails(hovered.Button, hovered.Element);
        });
        _hideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(400), _thumbnails.Hide);

        _slideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(16), SlideStep);
        _slideTimer.IsRepeating = true;
        if (_autoHide)
        {
            _autoHideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(250), CheckAutoHide);
            _autoHideTimer.IsRepeating = true;
            _autoHideTimer.Start();
            // The sliver left on screen catches the pointer.
            Root.PointerEntered += (_, _) => Reveal();
        }

        // The tray lives on the primary taskbar only, as in Windows 11.
        _tray = monitor.IsPrimary ? owner.Tray : null;
        _trayHoverTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(400), () =>
        {
            if (_trayHovered is { } icon)
            {
                _trayPopupOpen = true;
                _tray?.Send(icon, TrayMouseEvent.HoverStart);
            }
        });
        _indicators = monitor.IsPrimary ? owner.Indicators : null;
        if (_indicators is not null)
        {
            IndicatorArea.Visibility = Visibility.Visible;
            _indicators.Changed += RefreshIndicators;
            RefreshIndicators();
        }

        if (_tray is not null)
        {
            TrayArea.Visibility = Visibility.Visible;
            TrayIcons.ItemsSource = OverflowIcons.ItemsSource = _tray.Icons;
            _tray.Icons.CollectionChanged += OnTrayIconsChanged;
            RefreshTray();
        }

        Closed += (_, _) =>
        {
            if (_tray is not null)
                _tray.Icons.CollectionChanged -= OnTrayIconsChanged;
            if (_indicators is not null)
                _indicators.Changed -= RefreshIndicators;
            _autoHideTimer?.Stop();
            _slideTimer.Stop();
            _trayHoverTimer.Stop();
            _hoverTimer.Stop();
            _hideTimer.Stop();
            _thumbnails.Close();
            // Give the space back first, so windows can use it straight away.
            if (_appBar is not null)
                _appBar.Dispose();
            else if (!_autoHide)
                WorkArea.Set(monitor.Bounds);
            _placement.Dispose();
            _messages.Dispose();
            _frameless.Dispose();
        };
    }

    public DisplayMonitor Monitor => _monitor;

    /// <summary>Where the taskbar is on screen when shown (an auto-hidden one may be slid away), in pixels.</summary>
    public RectInt32 ScreenBounds => _shownBounds;

    /// <summary>Slides an auto-hidden taskbar back into view.</summary>
    public void Reveal()
    {
        _pointerAwayTicks = 0;
        if (!_hidden)
            return;

        _hidden = false;
        SlideTo(_shownBounds);
    }

    // Auto-hide: slide away once the pointer has been off the taskbar for a moment and nothing of it is in use.
    private void CheckAutoHide()
    {
        if (_hidden)
            return;

        RectInt32 shown = _shownBounds;
        PointInt32 pointer = Cursor.Position();
        bool pointerOver = pointer.X >= shown.X && pointer.X < shown.X + shown.Width
            && pointer.Y >= shown.Y && pointer.Y < shown.Y + shown.Height;
        bool inUse = pointerOver
            || _isActive // Win+T
            || VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Count > 0 // menus, calendar, flyouts
            || _thumbnails.Button is not null
            || _owner.IsStartMenuOpen;
        _pointerAwayTicks = inUse ? 0 : _pointerAwayTicks + 1;
        if (_pointerAwayTicks >= 3)
        {
            _hidden = true;
            _thumbnails.Hide();
            SlideTo(TaskbarLayout.HiddenBounds(shown));
        }
    }

    private void SlideTo(RectInt32 target)
    {
        _slideFrom = _placement.Bounds;
        _slideTo = target;
        _slideStep = 0;
        _slideTimer.Start();
    }

    private void SlideStep()
    {
        const int Steps = 8;
        _slideStep++;
        double progress = (double)_slideStep / Steps;
        _placement.Bounds = _slideTo with { Y = (int)Math.Round(_slideFrom.Y + (_slideTo.Y - _slideFrom.Y) * progress) };
        if (_slideStep >= Steps)
            _slideTimer.Stop();
    }

    /// <summary>
    /// Win+1…9: launches the Nth button's app, or acts like a click on it; with several windows, each press brings the
    /// next one to the front.
    /// </summary>
    public void ActivateTask(int index)
    {
        if (index >= _tasks.Count)
            return;

        Reveal();
        TaskButton button = _tasks[index];
        if (button.Windows.Count <= 1)
            ActivateButton(button);
        else
            TopLevelWindows.Activate(TaskActivation.NextWindow([.. button.Windows.Select(w => w.Handle)], _owner.Tracker.Foreground));
    }

    public void SetTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        _backdrop.Theme = theme;
    }

    public void UpdateClock() => Clock.Update();

    /// <summary>
    /// Makes way for a full-screen window on this monitor (the taskbar goes just below it), or with 0 returns to the
    /// topmost band.
    /// </summary>
    public void SetFullScreenWindow(nint window)
    {
        if (window == _fullScreenWindow)
            return;

        _fullScreenWindow = window;
        if (window == 0)
            _placement.SetLayer(PinnedLayer.Topmost);
        else
            _placement.SetLayer(PinnedLayer.Normal, above: window);
        Log.Info(window == 0 ? "Full-screen app left; taskbar back on top" : $"Full-screen app 0x{window:X}; taskbar makes way");
    }

    /// <summary>Activates the taskbar and focuses its first task button (or Start), for the arrow keys and Enter.</summary>
    public void FocusTaskList()
    {
        // A no-activate window can't become active, and only the active window gets the keyboard.
        WindowStyles.RemoveExtended(_hwnd, ExtendedWindowStyles.NoActivate);
        TopLevelWindows.Activate(_hwnd);

        if (_tasks.Count > 0 && TaskList.ContainerFromIndex(0) is Control first)
            first.Focus(FocusState.Keyboard);
        else
            StartButton.Focus(FocusState.Keyboard);
    }

    /// <summary>Shows the tray icons on the taskbar, or behind the chevron, as the tray mode setting says.</summary>
    public void RefreshTray()
    {
        if (_tray is null)
            return;

        bool showAll = _owner.Settings.Current.TrayMode == TrayMode.ShowAll;
        TrayIcons.Visibility = showAll ? Visibility.Visible : Visibility.Collapsed;
        OverflowButton.Visibility = !showAll && _tray.Icons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Where a tray icon is on screen: the icon itself, or the chevron when it's in the overflow (as Explorer
    /// answers for icons it doesn't show).
    /// </summary>
    public RectInt32? TrayIconBounds(TrayIcon icon)
    {
        if (_tray is null)
            return null;

        FrameworkElement? element = TrayIcons.Visibility == Visibility.Visible
            ? TrayIcons.ContainerFromItem(icon) as FrameworkElement
            : OverflowButton;
        return element is null ? null : BoundsOnScreen(element);
    }

    /// <summary>An element's place on screen, in pixels.</summary>
    private RectInt32 BoundsOnScreen(FrameworkElement element)
    {
        double scale = Root.XamlRoot.RasterizationScale;
        Rect rect = element.TransformToVisual(Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        RectInt32 taskbar = _placement.Bounds;
        return new RectInt32(
            taskbar.X + (int)(rect.X * scale), taskbar.Y + (int)(rect.Y * scale), (int)(rect.Width * scale), (int)(rect.Height * scale));
    }

    private void RefreshIndicators()
    {
        if (_indicators is null)
            return;

        NetworkIcon.Glyph = IndicatorDisplay.NetworkGlyph(_indicators.Network);
        SetToolTip(NetworkButton, IndicatorDisplay.NetworkToolTip(_indicators.Network));

        float volume = _indicators.Volume;
        bool muted = _indicators.IsMuted;
        VolumeIcon.Glyph = MuteIcon.Glyph = IndicatorDisplay.VolumeGlyph(_indicators.HasAudioDevice, volume, muted);
        SetToolTip(VolumeButton, IndicatorDisplay.VolumeToolTip(_indicators.AudioDeviceName, volume, muted));
        VolumeDeviceName.Text = _indicators.AudioDeviceName ?? "No audio output device";
        VolumeText.Text = IndicatorDisplay.Percent(volume).ToString();
        AutomationProperties.SetName(MuteButton, muted ? "Unmute" : "Mute");
        // Moving the slider changes the volume, which comes back here; don't set it back while it's being dragged.
        _updatingVolumeSlider = true;
        VolumeSlider.Value = IndicatorDisplay.Percent(volume);
        _updatingVolumeSlider = false;

        IReadOnlyList<string> apps = _indicators.MicrophoneApps;
        MicrophoneButton.Visibility = apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetToolTip(MicrophoneButton, IndicatorDisplay.MicrophoneToolTip(apps));
    }

    private static void SetToolTip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text.Replace('\n', ' '));
    }

    private void Network_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenSettings(_owner.RunMode, "Network settings", "ms-settings:network", "ncpa.cpl");

    // As the shell: the Recording tab of Sound, as Control Panel has no microphone privacy page.
    private void Microphone_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenSettings(_owner.RunMode, "Microphone privacy settings", "ms-settings:privacy-microphone", "mmsys.cpl,,1");

    private void SoundSettings_Click(object sender, RoutedEventArgs e)
    {
        VolumeFlyout.Hide();
        Launcher.OpenSettings(_owner.RunMode, "Sound settings", "ms-settings:sound", "mmsys.cpl");
    }

    private void VolumeFlyout_Opening(object sender, object e) => RefreshIndicators();

    private void Volume_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_indicators is null)
            return;

        _indicators.Volume = IndicatorDisplay.WheelVolume(_indicators.Volume, e.GetCurrentPoint(VolumeButton).Properties.MouseWheelDelta);
        e.Handled = true;
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_indicators is null || _updatingVolumeSlider)
            return;

        _indicators.Volume = (float)(e.NewValue / 100);
        // Turning it up means wanting to hear it, as in Windows' own volume slider.
        if (_indicators.IsMuted && e.NewValue > 0)
            _indicators.IsMuted = false;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_indicators is not null)
            _indicators.IsMuted = !_indicators.IsMuted;
    }

    private void OnTrayIconsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshTray();

    private void TrayIcon_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_tray is null || sender is not FrameworkElement { DataContext: TrayIcon icon } element)
            return;

        PointerPointProperties button = e.GetCurrentPoint(element).Properties;
        TrayMouseEvent mouseEvent;
        if (button.IsLeftButtonPressed)
        {
            // Windows turns a second press within the double-click time into a double-click; so does the tray.
            long now = Environment.TickCount64;
            bool doubleClick = _lastTrayLeftDown is { } last && last.Icon == icon && now - last.Time <= NotifyIconInput.DoubleClickTime;
            mouseEvent = doubleClick ? TrayMouseEvent.LeftDoubleClick : TrayMouseEvent.LeftDown;
            _lastTrayLeftDown = doubleClick ? null : (icon, now);
        }
        else if (button.IsRightButtonPressed)
        {
            mouseEvent = TrayMouseEvent.RightDown;
        }
        else if (button.IsMiddleButtonPressed)
        {
            mouseEvent = TrayMouseEvent.MiddleDown;
        }
        else
        {
            return;
        }

        element.CapturePointer(e.Pointer);
        EndTrayHover();
        _tray.Send(icon, mouseEvent);
        e.Handled = true;
    }

    private void TrayIcon_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_tray is null || sender is not FrameworkElement { DataContext: TrayIcon icon } element)
            return;

        element.ReleasePointerCapture(e.Pointer);
        TrayMouseEvent? mouseEvent = e.GetCurrentPoint(element).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonReleased => TrayMouseEvent.LeftUp,
            PointerUpdateKind.RightButtonReleased => TrayMouseEvent.RightUp,
            PointerUpdateKind.MiddleButtonReleased => TrayMouseEvent.MiddleUp,
            _ => null,
        };
        if (mouseEvent is not { } released)
            return;

        _tray.Send(icon, released);
        // Out of the way of the menu or window the app opens.
        if (released is TrayMouseEvent.LeftUp or TrayMouseEvent.RightUp)
            OverflowFlyout.Hide();
        e.Handled = true;
    }

    private void TrayIcon_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TrayIcon icon } element)
            return;

        // A captured pointer keeps reporting after leaving the icon; the app only cares about moves over it.
        Point point = e.GetCurrentPoint(element).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight)
            _tray?.Send(icon, TrayMouseEvent.Move);
    }

    private void TrayIcon_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid { DataContext: TrayIcon icon } element)
            return;

        element.Children[0].Opacity = 1; // hover background
        _trayHovered = icon;
        _trayHoverTimer.Start();
    }

    private void TrayIcon_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid element)
            element.Children[0].Opacity = 0;
        EndTrayHover();
    }

    private void EndTrayHover()
    {
        _trayHoverTimer.Stop();
        if (_trayPopupOpen && _trayHovered is { } icon)
            _tray?.Send(icon, TrayMouseEvent.HoverEnd);
        _trayPopupOpen = false;
        _trayHovered = null;
    }

    // A right-click on an icon is the app's; the taskbar's own menu must not open too.
    private void TrayIcon_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => e.Handled = true;

    private void ShowAllTrayIcons_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { TrayMode = ShowAllTrayIconsItem.IsChecked ? TrayMode.ShowAll : TrayMode.Overflow });

    /// <summary>Rebuilds the task buttons from the pinned apps and the tracked windows.</summary>
    public void RefreshTasks()
    {
        ShellSettings settings = _owner.Settings.Current;
        IReadOnlyList<WindowInfo> windows = _owner.Tracker.Windows;
        double available = AvailableTaskWidth();
        // More buttons than fit are cut off rather than drawn over the clock.
        TaskList.MaxWidth = Math.Max(0, available);
        IReadOnlyList<TaskButtonModel> uncombined = TaskListBuilder.Build(settings.PinnedTaskbarApps, windows, combine: false);
        bool combine = TaskListBuilder.ShouldCombine(settings.CombineButtons, uncombined, available);
        IReadOnlyList<TaskButtonModel> models = combine
            ? TaskListBuilder.Build(settings.PinnedTaskbarApps, windows, combine: true)
            : uncombined;

        // Update in place, so buttons keep their state and the list doesn't flicker. Gone buttons are removed first:
        // otherwise every button after one would be moved up a place, and the list animates each move as a new item.
        var keys = models.Select(model => model.Key).ToHashSet();
        for (int i = _tasks.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_tasks[i].Key))
                _tasks.RemoveAt(i);
        }
        for (int i = 0; i < models.Count; i++)
        {
            int existing = IndexOf(models[i].Key, i);
            if (existing < 0)
                _tasks.Insert(i, new TaskButton(models[i].Key));
            else if (existing != i)
                _tasks.Move(existing, i);
            _tasks[i].Update(models[i], _owner.Tracker, combine);
        }

        // The previews show windows that may have closed or opened.
        if (_thumbnails.Button is { } shown && !_tasks.Contains(shown))
            _thumbnails.Hide();
    }

    private double AvailableTaskWidth()
    {
        // Centred, the task list must stay clear of the right-hand panel on both sides to stay centred.
        double right = AppsPanel.HorizontalAlignment == HorizontalAlignment.Center ? 2 * RightPanel.ActualWidth : RightPanel.ActualWidth;
        double buttons = SearchButton.Visibility == Visibility.Visible ? 2 * FixedButtonWidth : FixedButtonWidth;
        return Root.ActualWidth - right - buttons - AppsPanelMargins;
    }

    private int IndexOf(string key, int start)
    {
        for (int i = start; i < _tasks.Count; i++)
        {
            if (_tasks[i].Key == key)
                return i;
        }
        return -1;
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        // WinUI resizes the window for the new DPI; the taskbar needs a new height, so it's recreated. A window
        // moved onto its monitor right after creation gets this too, with the DPI it was created for.
        if (message == WindowMessages.DpiChanged && (uint)(wParam & 0xFFFF) != _monitor.Dpi)
            _owner.QueueRecreate();
        else
            _owner.OnBroadcast(message);
        return null;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => _owner.ToggleStartMenu(this);

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element == StartButton || element == SearchButton)
                return;
        }
        _owner.HideStartMenu();
    }

    private void TaskList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskButton button)
            ActivateButton(button);
    }

    private void ActivateButton(TaskButton button)
    {
        if (KeyboardState.IsShiftDown() || button.Windows.Count == 0)
        {
            _thumbnails.Hide();
            Launcher.Launch(button.App);
        }
        else if (button.Windows.Count == 1)
        {
            _thumbnails.Hide();
            nint hwnd = button.Windows[0].Handle;
            if (hwnd == _owner.Tracker.Foreground && !TopLevelWindows.IsMinimized(hwnd))
                TopLevelWindows.MinimizeAndActivateNext(hwnd);
            else
                TopLevelWindows.Activate(hwnd);
        }
        else if (_thumbnails.Button == button)
        {
            _thumbnails.Hide();
        }
        else if (TaskList.ContainerFromItem(button) is FrameworkElement element)
        {
            ShowThumbnails(button, element);
        }
    }

    private void TaskItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskButton button } element
            && e.GetCurrentPoint(element).Properties.IsMiddleButtonPressed)
        {
            Launcher.Launch(button.App);
            e.Handled = true;
        }
    }

    private void TaskItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskButton button } element)
            return;

        _hovered = (button, element);
        button.IsHovered = true;
        _hideTimer.Stop();
        // Once previews are open, moving along the taskbar switches them straight away.
        if (_thumbnails.Button is not null && _thumbnails.Button != button && button.Windows.Count > 0)
            ShowThumbnails(button, element);
        else
            _hoverTimer.Start();
    }

    private void TaskItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskButton button })
            button.IsHovered = false;
        _hovered = null;
        _hoverTimer.Stop();
        _hideTimer.Start();
    }

    private void ShowThumbnails(TaskButton button, FrameworkElement element)
    {
        _thumbnails.Show(button, BoundsOnScreen(element), _monitor, Root.ActualTheme);
    }

    private void TaskMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        if (menu.Target?.DataContext is not TaskButton button)
            return;

        _thumbnails.Hide();
        menu.Items.Clear();
        menu.Items.Add(MenuItem(button.App.DisplayName, "", "TaskLaunchMenuItem", () => Launcher.Launch(button.App)));
        menu.Items.Add(button.Pinned is { } pinned
            ? MenuItem("Unpin from taskbar", "", "TaskUnpinMenuItem", () => _owner.Unpin(pinned))
            : MenuItem("Pin to taskbar", "", "TaskPinMenuItem", () => _owner.Pin(button.App)));
        if (button.Windows.Count > 0)
        {
            IReadOnlyList<WindowInfo> windows = button.Windows;
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(windows.Count == 1 ? "Close window" : "Close all windows", "", "TaskCloseMenuItem", () =>
            {
                foreach (WindowInfo window in windows)
                    TopLevelWindows.Close(window.Handle);
            }));
        }
    }

    private void TaskList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Only pinned apps keep their place; running apps fall back in line on the next refresh.
        _owner.SetPinnedOrder([.. _tasks.Select(t => t.Pinned).OfType<PinnedApp>().DistinctBy(TaskGrouping.Key)]);
    }

    private void TaskbarMenu_Opening(object sender, object e)
    {
        ShellSettings settings = _owner.Settings.Current;
        AutoHideItem.IsChecked = settings.AutoHide;
        AlignCenterItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Center;
        AlignLeftItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Left;
        ShowSearchItem.IsChecked = settings.ShowSearchButton;
        CombineAlwaysItem.IsChecked = settings.CombineButtons == CombineButtons.Always;
        CombineWhenFullItem.IsChecked = settings.CombineButtons == CombineButtons.WhenFull;
        CombineNeverItem.IsChecked = settings.CombineButtons == CombineButtons.Never;
        AllDisplaysItem.IsChecked = settings.ShowOnAllDisplays;
        ShowAllTrayIconsItem.Visibility = _tray is not null ? Visibility.Visible : Visibility.Collapsed;
        ShowAllTrayIconsItem.IsChecked = settings.TrayMode == TrayMode.ShowAll;
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e) => _owner.OpenTaskManager();

    private void Alignment_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            TaskbarAlignment = ReferenceEquals(sender, AlignLeftItem) ? TaskbarAlignment.Left : TaskbarAlignment.Center,
        });

    private void ShowSearch_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { ShowSearchButton = ShowSearchItem.IsChecked });

    private void Combine_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            CombineButtons = ReferenceEquals(sender, CombineNeverItem) ? CombineButtons.Never
                : ReferenceEquals(sender, CombineWhenFullItem) ? CombineButtons.WhenFull
                : CombineButtons.Always,
        });

    private void AutoHide_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { AutoHide = AutoHideItem.IsChecked });

    private void AllDisplays_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { ShowOnAllDisplays = AllDisplaysItem.IsChecked });

    private void Exit_Click(object sender, RoutedEventArgs e) => _owner.Exit();

    private void ShowDesktopButton_Click(object sender, RoutedEventArgs e) => _owner.ToggleDesktop();

    private static MenuFlyoutItem MenuItem(string text, string glyph, string automationId, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static DispatcherQueueTimer CreateTimer(DispatcherQueue dispatcher, TimeSpan interval, Action onTick)
    {
        DispatcherQueueTimer timer = dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => onTick();
        return timer;
    }
}
