using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NeoShell.Interop.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using NeoShell.Interop.Input;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Logging;
using NeoShell.QuickSettings;
using NeoShell.Settings;
using NeoShell.Tray;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
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
    private ShellBackdrop _backdrop;
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private readonly AppBar? _appBar;
    private readonly ObservableCollection<TaskButton> _tasks = [];
    private readonly ThumbnailPopup _thumbnails;
    private readonly MenuFlyout _quickLinks;
    private readonly DispatcherQueueTimer _hoverTimer;
    private readonly DispatcherQueueTimer _hideTimer;
    private (TaskButton Button, FrameworkElement Element)? _hovered;
    private (uint PointerId, double X, TaskButton Button)? _pressed;
    // A right press shrinks the icon as a left one does, but never drags.
    private TaskButton? _rightPressed;
    private TaskDrag? _drag;
    private readonly List<(UIElement Container, string Property, long Started)> _layoutAnimations = [];
    // A drag from another app or the desktop (TaskbarDrop): the button it hovers, the app it would pin and the gap
    // opened for it.
    private readonly DispatcherQueueTimer _dragHoverTimer;
    private TaskButton? _dragHovered;
    private PinnedApp? _dragApp;
    private DropGap? _dropGap;
    private static readonly TimeSpan s_layoutAnimationDuration = TimeSpan.FromMilliseconds(250);
    // Measured on Explorer: open previews move to another button ~200 ms after the pointer enters it, moving or not.
    private static readonly TimeSpan s_previewDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_previewSwitchDelay = TimeSpan.FromMilliseconds(200);
    private bool _suppressClick;
    // A right-clicked button shows no previews until the pointer has left it, and none show while its menu is open:
    // a hover that began before the click would otherwise open them over the menu.
    private TaskButton? _noPreviews;
    private bool _taskMenuOpen;
    // The taskbar around the Start button acts as the button (TaskbarLayout.IsStartZone): hovered, or pressed by
    // this pointer.
    private bool _startZoneHovered;
    private uint? _startZonePointer;
    private readonly NotificationArea? _tray;
    private readonly Indicators? _indicators;
    private QuickSettingsPage _quickSettingsPage;
    // Explorer's input switcher slides in (in about 67 ms) and out faster than its other flyouts. A popup window of
    // ours shows on screen some 40 ms after it starts to slide, so its slide takes longer for the same look.
    private static readonly TimeSpan s_inputSwitcherOpening = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan s_inputSwitcherClosing = TimeSpan.FromMilliseconds(100);
    // Whether the switcher is Win+Space's, which switches when the Windows key is let go of.
    private bool _inputSwitcherCompact;
    private InputMethod? _inputMethodToSwitch;
    private readonly DispatcherQueueTimer _trayHoverTimer;
    private TrayIcon? _trayHovered;
    private bool _trayPopupOpen;
    private (TrayIcon Icon, long Time)? _lastTrayLeftDown;
    private TrayPress? _trayPress;
    // As Explorer's tray: how far a press moves to become a drag, the dragged icon left in its place, and how fast a
    // dropped one grows in (5 frames).
    private const double TrayDragThreshold = 4;
    private const double TrayDragPlaceholderOpacity = 0.3;
    private static readonly TimeSpan TrayDropGrowDuration = TimeSpan.FromMilliseconds(83);
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
    private ElementTheme _theme;
    private Color? _accent;
    private int _pointerAwayTicks;

    public TaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings, ElementTheme theme, Color? accent)
    {
        _owner = owner;
        _monitor = monitor;
        InitializeComponent();
        _quickLinks = QuickLinkMenu.Create(owner, this);
        _quickLinks.MenuFlyoutPresenterStyle = (Style)Root.Resources["ShellMenuPresenterStyle"];

        _backdrop = new ShellBackdrop(settings.TaskbarBackdrop);
        SetTheme(theme, accent);
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
        // Alt+F4 while it has the keyboard (Win+T) would close it; Explorer's taskbar asks to shut down instead.
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            owner.ShowShutDownDialog();
        };
        _frameless = new FramelessWindow(hwnd);
        Peek.Exclude(hwnd);
        SystemBackdrop = _backdrop;
        WindowTransparency.SetSeeThrough(hwnd, _backdrop.Kind is Backdrop.Translucent or Backdrop.Transparent);
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
            ShellWorkArea.ReserveBottom(monitor.Bounds, monitor.Bounds.Y + monitor.Bounds.Height - bounds.Y);
        }
        _shownBounds = bounds;
        _placement = new PinnedWindow(hwnd, bounds, PinnedLayer.Topmost);

        TaskList.ItemsSource = _tasks;
        IconPress.Attach(StartButton, (UIElement)StartButton.Content);
        IconPress.Attach(SearchButton, (UIElement)SearchButton.Content);
        IconPress.Attach(OverflowButton, OverflowChevron);
        // Presses on the taskbar's tray icons reach them while the overflow is open (see OverflowFlyout_Closing).
        OverflowFlyout.OverlayInputPassThroughElement = TrayIcons;
        // Handled events too: the button under the pointer takes the press for its click.
        TaskList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TaskList_PointerPressed), handledEventsToo: true);
        TaskList.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(TaskList_PointerMoved), handledEventsToo: true);
        TaskList.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TaskList_PointerReleased), handledEventsToo: true);
        TaskList.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(TaskList_PointerCaptureLost), handledEventsToo: true);
        // A click anywhere else on the taskbar closes Start, as in Windows; the Start button toggles it itself.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), handledEventsToo: true);
        Root.PointerMoved += Root_PointerMoved;
        Root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Root_PointerReleased), handledEventsToo: true);
        Root.PointerExited += (_, _) => SetStartZoneHover(false);
        Root.PointerCaptureLost += (_, _) => EndStartZonePress();
        Root.SizeChanged += (_, _) => RefreshTasks();
        RightPanel.SizeChanged += (_, _) => RefreshTasks();

        _thumbnails = new ThumbnailPopup(owner.Tracker, hwnd);
        _thumbnails.Root.PointerEntered += (_, _) => _hideTimer!.Stop();
        _thumbnails.Root.PointerExited += (_, _) => _hideTimer!.Start();
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _hoverTimer = CreateTimer(dispatcher, s_previewDelay, () =>
        {
            if (_hovered is { } hovered && hovered.Button.Windows.Count > 0 && CanPreview(hovered.Button))
                ShowThumbnails(hovered.Button, hovered.Element);
        });
        _hideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(400), _thumbnails.Hide);
        _dragHoverTimer = CreateTimer(dispatcher, TaskbarDrop.ButtonHoverDelay, DragHoverElapsed);
        _thumbnails.DragEntered += () => _hideTimer.Stop();
        _thumbnails.DragLeft += () => _hideTimer.Start();
        Root.AllowDrop = true;
        Root.DragEnter += Root_DragEnter;
        Root.DragOver += (_, e) => DragOverTaskbar(e);
        Root.DragLeave += (_, _) => EndDragOver();
        Root.Drop += Root_Drop;

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
            QuickSettings.Attach(_indicators, owner.RunMode, owner.Icons);
            QuickSettings.CloseRequested += QuickSettingsFlyout.Hide;
            InputSwitcher.MethodChosen += method =>
            {
                _inputMethodToSwitch = method;
                InputFlyout.Hide();
            };
            InputSwitcher.SettingsRequested += () =>
            {
                InputFlyout.Hide();
                // As the shell, Windows' classic Text Services and Input Languages: Settings can't start without Explorer.
                Launcher.OpenSettings(
                    owner.RunMode, "Keyboard settings", "ms-settings:regionlanguage", "input.dll,,{C07337D3-DB2C-4D0B-9A93-B722A6C106E2}");
            };
            _indicators.Changed += RefreshIndicators;
            RefreshIndicators();
        }

        Clock.Clicked += () => owner.ToggleClockFlyout(this);
        if (owner.Notifications is { } notifications)
        {
            notifications.DoNotDisturbChanged += ShowDoNotDisturb;
            ShowDoNotDisturb();
        }

        if (_tray is not null)
        {
            TrayArea.Visibility = Visibility.Visible;
            TrayIcons.ItemsSource = _tray.PromotedIcons;
            OverflowIcons.ItemsSource = _tray.OverflowIcons;
            _tray.PromotedIcons.CollectionChanged += OnTrayIconsChanged;
            _tray.OverflowIcons.CollectionChanged += OnTrayIconsChanged;
            _tray.ChevronVisibilityChanged += RefreshTray;
            RefreshTray();
        }

        Closed += (_, _) =>
        {
            if (_tray is not null)
            {
                _tray.PromotedIcons.CollectionChanged -= OnTrayIconsChanged;
                _tray.OverflowIcons.CollectionChanged -= OnTrayIconsChanged;
                _tray.ChevronVisibilityChanged -= RefreshTray;
            }
            if (owner.Notifications is { } notifications)
                notifications.DoNotDisturbChanged -= ShowDoNotDisturb;
            if (_indicators is not null)
            {
                _indicators.Changed -= RefreshIndicators;
                QuickSettings.Detach();
            }
            _autoHideTimer?.Stop();
            _slideTimer.Stop();
            _trayHoverTimer.Stop();
            _hoverTimer.Stop();
            _hideTimer.Stop();
            _dragHoverTimer.Stop();
            _thumbnails.Close();
            // Give the space back first, so windows can use it straight away.
            if (_appBar is not null)
                _appBar.Dispose();
            else if (!_autoHide)
                ShellWorkArea.ReserveBottom(monitor.Bounds, 0);
            _placement.Dispose();
            _messages.Dispose();
            _frameless.Dispose();
        };
    }

    public DisplayMonitor Monitor => _monitor;

    public nint Handle => _hwnd;

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
            || VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Count > 0 // menus and flyouts
            || _thumbnails.Button is not null
            || _owner.IsStartMenuOpen
            || _owner.IsClockFlyoutOpen;
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

    /// <summary>Win+Shift+1…9 starts another instance of the Nth button's app; Win+Ctrl+Shift+1…9 as administrator.</summary>
    public void LaunchTask(int index, bool elevated)
    {
        if (index < _tasks.Count)
            Launcher.Launch(_tasks[index].App, elevated);
    }

    /// <summary>
    /// Win+Ctrl+1…9: the Nth button's window that was in front last, then round its windows; starts the app when
    /// it has none.
    /// </summary>
    public void ActivateLastWindow(int index)
    {
        if (index >= _tasks.Count)
            return;

        TaskButton button = _tasks[index];
        if (button.Windows.Count == 0)
        {
            Launcher.Launch(button.App);
            return;
        }
        Reveal();
        TopLevelWindows.Activate(TaskActivation.LastActiveWindow(
            [.. button.Windows.Select(w => w.Handle)], TopLevelWindows.GetAll(), _owner.Tracker.Foreground));
    }

    /// <summary>Win+Alt+1…9: the Nth button's menu (its jump list), with the keyboard in it.</summary>
    public void ShowJumpList(int index)
    {
        if (index >= _tasks.Count
            || TaskList.ContainerFromIndex(index) is not ListViewItem { ContentTemplateRoot: FrameworkElement element }
            || FlyoutBase.GetAttachedFlyout(element) is not { } menu)
        {
            return;
        }
        Reveal();
        TakeKeyboard();
        TaskbarFlyouts.ShowCentered(menu, element);
    }

    /// <param name="accent">The taskbar's colour when Windows shows the accent colour on it.</param>
    public void SetTheme(ElementTheme theme, Color? accent)
    {
        _theme = theme;
        _accent = accent;
        Root.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = Root.RequestedTheme;
        _backdrop.Tint = accent;
        // Quick Settings and the hidden tray icons take the taskbar's colour, as Windows' do; their popup windows get a
        // backdrop of their own, acrylic whatever the taskbar's, as Windows' always is.
        QuickSettings.RequestedTheme = Root.RequestedTheme;
        QuickSettingsFlyout.SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = Root.RequestedTheme, Tint = accent };
        InputSwitcher.RequestedTheme = Root.RequestedTheme;
        InputFlyout.SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = Root.RequestedTheme, Tint = accent };
        OverflowIcons.RequestedTheme = Root.RequestedTheme;
        OverflowFlyout.SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = Root.RequestedTheme, Tint = accent };
        foreach (MenuFlyout menu in (MenuFlyout[])[TaskbarMenu, NetworkMenu, VolumeMenu, _quickLinks])
            menu.SystemBackdrop = MenuBackdrop();
    }

    // WinUI's own menu backdrop turns solid while the menu's window is inactive, as a menu of the taskbar (which
    // doesn't take the focus) nearly always is; Explorer's menus stay acrylic.
    private ShellBackdrop MenuBackdrop() => new(Backdrop.Acrylic) { Theme = Root.RequestedTheme };

    public void SetBackdrop(Backdrop kind)
    {
        if (kind == _backdrop.Kind)
            return;

        SystemBackdrop = _backdrop = new ShellBackdrop(kind);
        WindowTransparency.SetSeeThrough(_hwnd, kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(_theme, _accent);
    }

    public void UpdateClock() => Clock.Update();

    private void ShowDoNotDisturb() => Clock.ShowDoNotDisturb(_owner.Notifications?.DoNotDisturb == true);

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

    /// <summary>
    /// Activates the taskbar and focuses its first task button (Win+T) or its last (Win+Shift+T), or Start when
    /// there are none, for the arrow keys and Enter.
    /// </summary>
    public void FocusTaskList(bool last = false)
    {
        TakeKeyboard();

        if (_tasks.Count > 0 && TaskList.ContainerFromIndex(last ? _tasks.Count - 1 : 0) is Control button)
            button.Focus(FocusState.Keyboard);
        else
            StartButton.Focus(FocusState.Keyboard);
    }

    /// <summary>
    /// Win+B, on the primary taskbar: activates it and focuses the notification area: the chevron of the hidden
    /// icons, as Explorer does, or Quick Settings' button when there is no chevron.
    /// </summary>
    public void FocusTray()
    {
        TakeKeyboard();

        if (OverflowButton.Visibility == Visibility.Visible && TrayArea.Visibility == Visibility.Visible)
            OverflowButton.Focus(FocusState.Keyboard);
        else
            QuickSettingsButton.Focus(FocusState.Keyboard);
    }

    // A no-activate window can't become active, and only the active window gets the keyboard; once it's left, the
    // taskbar is no-activate again (see the Activated handler).
    private void TakeKeyboard()
    {
        WindowStyles.RemoveExtended(_hwnd, ExtendedWindowStyles.NoActivate);
        TopLevelWindows.Activate(_hwnd);
    }

    /// <summary>
    /// Win+X: opens the Quick Link menu above Start, with the keyboard in it for the arrow keys and Enter; closes it
    /// when it's open.
    /// </summary>
    public void ToggleQuickLinks()
    {
        if (_quickLinks.IsOpen)
        {
            _quickLinks.Hide();
            return;
        }
        Reveal();
        TakeKeyboard();
        ShowQuickLinks();
    }

    private void ShowQuickLinks()
    {
        _owner.HideStartMenu();
        TaskbarFlyouts.ShowAboveLeft(_quickLinks, StartButton);
    }

    private void StartButton_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        ShowQuickLinks();
    }

    /// <summary>Shows the chevron while there are hidden icons, unless the hidden icon menu is turned off.</summary>
    public void RefreshTray()
    {
        if (_tray is null)
            return;

        bool chevron = _tray.ChevronVisible && _tray.OverflowIcons.Count > 0;
        OverflowButton.Visibility = chevron ? Visibility.Visible : Visibility.Collapsed;
        // The overflow closes when its last icon leaves, as Explorer's does.
        if (!chevron)
            OverflowFlyout.Hide();
    }

    /// <summary>
    /// Where a tray icon is on screen: the icon itself, or the chevron when it's in the overflow (as Explorer
    /// answers for icons it doesn't show).
    /// </summary>
    public RectInt32? TrayIconBounds(TrayIcon icon)
    {
        if (_tray is null)
            return null;

        FrameworkElement? element = _tray.PromotedIcons.Contains(icon)
            ? TrayIcons.ContainerFromItem(icon) as FrameworkElement
            : OverflowButton.Visibility == Visibility.Visible ? OverflowButton : null;
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

        bool airplaneMode = _indicators.AirplaneMode == true;
        NetworkIcon.Glyph = IndicatorDisplay.NetworkGlyph(_indicators.Network, airplaneMode);
        SetToolTip(NetworkIndicator, IndicatorDisplay.NetworkToolTip(_indicators.Network, airplaneMode));

        float volume = _indicators.Volume;
        bool muted = _indicators.IsMuted;
        VolumeIcon.Glyph = IndicatorDisplay.VolumeGlyph(_indicators.HasAudioDevice, volume, muted);
        SetToolTip(VolumeIndicator, IndicatorDisplay.VolumeToolTip(_indicators.AudioDeviceName, volume, muted));

        BatteryIndicator.Visibility = _indicators.Battery is null ? Visibility.Collapsed : Visibility.Visible;
        if (_indicators.Battery is { } battery)
        {
            BatteryIcon.Glyph = QuickSettingsDisplay.BatteryGlyph(battery);
            SetToolTip(BatteryIndicator, QuickSettingsDisplay.BatteryToolTip(battery));
        }
        EnergySaverIndicator.Visibility =
            _indicators.IsEnergySaverOn && _indicators.Battery is null ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<string> apps = _indicators.MicrophoneApps;
        bool microphoneMuted = _indicators.IsMicrophoneMuted;
        MicrophoneButton.Visibility = apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneIcon.Glyph = microphoneMuted ? IndicatorDisplay.MicrophoneMutedGlyph : IndicatorDisplay.MicrophoneGlyph;
        SetToolTip(MicrophoneButton, IndicatorDisplay.MicrophoneToolTip(apps, microphoneMuted));

        RefreshInputIndicator();
    }

    private void RefreshInputIndicator()
    {
        if (_indicators?.InputMethod is not { } current)
        {
            InputButton.Visibility = ImeModeButton.Visibility = Visibility.Collapsed;
            if (InputFlyout.IsOpen)
                InputFlyout.Hide();
            return;
        }

        InputButton.Visibility = Visibility.Visible;
        (string? glyph, string language, string keyboard) = IndicatorDisplay.InputLabel(current, _indicators.InputMethodInFront);
        InputGlyph.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        InputCodes.Visibility = glyph is null ? Visibility.Visible : Visibility.Collapsed;
        InputGlyph.Glyph = glyph ?? "";
        InputLanguageText.Text = language;
        InputKeyboardText.Text = keyboard;
        InputKeyboardText.Visibility = keyboard.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetToolTip(InputButton, IndicatorDisplay.InputToolTip(current, _indicators.InputMethodInFront));

        ImeModeButton.Visibility = _indicators.ImeMode is null ? Visibility.Collapsed : Visibility.Visible;
        if (_indicators.ImeMode is { } mode)
        {
            ImeModeIcon.Glyph = mode.Glyph;
            SetToolTip(ImeModeButton, mode.ToolTip);
        }
    }

    // As Explorer's: a click opens the switcher with the input method in front chosen, or closes it.
    private void InputButton_Click(object sender, RoutedEventArgs e)
    {
        if (InputFlyout.IsOpen)
            InputFlyout.Hide();
        else
            ShowInputSwitcher(compact: false);
    }

    private void ShowInputSwitcher(bool compact, bool backwards = false)
    {
        if (_indicators is null)
            return;

        _indicators.RefreshInputMethods();
        IReadOnlyList<InputMethod> methods = _indicators.EnabledInputMethods;
        int inFront = _indicators.InputMethodInFront is { } method ? IndexOf(methods, method) : -1;
        // Win+Space moves on at once: the switcher opens with the next input method chosen.
        InputSwitcher.Show(methods, compact ? IndicatorDisplay.NextInputMethod(methods.Count, inFront, backwards) : inFront, compact);
        _inputSwitcherCompact = compact;
        if (!InputFlyout.IsOpen)
        {
            Reveal();
            TaskbarFlyouts.ShowAtRight(InputFlyout, InputButton, s_inputSwitcherOpening, s_inputSwitcherClosing);
        }
    }

    private static int IndexOf(IReadOnlyList<InputMethod> methods, InputMethod method)
    {
        for (int i = 0; i < methods.Count; i++)
        {
            if (methods[i] == method)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Win+Space as the shell: shows the input methods with the next one chosen, moves on with each Space, and
    /// switches to the chosen one when the Windows key is let go of, as Explorer's switcher does.
    /// </summary>
    public void RunInputSwitch(InputSwitchCommand command)
    {
        if (_indicators?.InputMethod is null)
            return;

        bool backwards = command is InputSwitchCommand.OpenBackwards or InputSwitchCommand.Previous;
        switch (command)
        {
            case InputSwitchCommand.Open or InputSwitchCommand.OpenBackwards:
                ShowInputSwitcher(compact: true, backwards);
                break;
            case InputSwitchCommand.Next or InputSwitchCommand.Previous when InputFlyout.IsOpen:
                InputSwitcher.Select(IndicatorDisplay.NextInputMethod(_indicators.EnabledInputMethods.Count, InputSwitcher.SelectedIndex, backwards));
                break;
            case InputSwitchCommand.Commit when InputFlyout.IsOpen && _inputSwitcherCompact:
                _inputMethodToSwitch = InputSwitcher.Selected;
                InputFlyout.Hide();
                break;
        }
    }

    private void InputFlyout_Opened(object sender, object e)
    {
        InputOpenPlate.Visibility = Visibility.Visible;
        // The switcher takes the focus as it opens, and Windows switches the app when it gets it back; but Win+Space's
        // switcher shows no focus rectangle, as Explorer's.
        if (_inputSwitcherCompact)
            InputSwitcher.HideFocusVisual();
    }

    private void InputFlyout_Closed(object sender, object e)
    {
        InputOpenPlate.Visibility = Visibility.Collapsed;
        _inputSwitcherCompact = false;
        // Only now: Windows switches the app that has the focus, which the switcher held while it was open.
        if (_inputMethodToSwitch is { } method)
        {
            _inputMethodToSwitch = null;
            _indicators?.SwitchInputMethod(method);
        }
    }

    // Not the taskbar's menu. Explorer opens the IME's menu here, which the input switcher draws in a window only
    // Explorer may create.
    private void ImeMode_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => e.Handled = true;

    // Passed on to the IME, as Explorer does: it switches its mode.
    private void ImeMode_Click(object sender, RoutedEventArgs e)
    {
        double scale = Root.XamlRoot.RasterizationScale;
        Point topLeft = ImeModeButton.TransformToVisual(Root).TransformPoint(default);
        var bounds = new RectInt32(
            _shownBounds.X + (int)Math.Round(topLeft.X * scale), _shownBounds.Y + (int)Math.Round(topLeft.Y * scale),
            (int)Math.Round(ImeModeButton.ActualWidth * scale), (int)Math.Round(ImeModeButton.ActualHeight * scale));
        _indicators?.ClickImeMode(Cursor.Position(), bounds);
    }

    private void QuickSettingsButton_Click(object sender, RoutedEventArgs e) => ShowQuickSettings(QuickSettingsPage.Main);

    /// <summary>
    /// Opens Quick Settings on a page, as Win+A (the tiles), Win+Ctrl+V (Sound output), Win+K (Cast) and Win+P
    /// (Project) do; closes it when it's open on that page already.
    /// </summary>
    public void ShowQuickSettings(QuickSettingsPage page)
    {
        if (_indicators is null)
            return;

        if (QuickSettingsFlyout.IsOpen)
        {
            if (page == _quickSettingsPage)
                QuickSettingsFlyout.Hide();
            else
                QuickSettings.Navigate(page);
            _quickSettingsPage = page;
            return;
        }
        _quickSettingsPage = page;
        Reveal();
        TaskbarFlyouts.ShowAtRight(QuickSettingsFlyout, QuickSettingsButton);
    }

    // The icon under the pointer gets its own menu; from the keyboard, the speaker's.
    private void QuickSettingsButton_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        bool network = e.TryGetPosition(NetworkIndicator, out Point point)
            && point.X >= 0 && point.X < NetworkIndicator.ActualWidth;
        if (network)
            TaskbarFlyouts.ShowCentered(NetworkMenu, NetworkIndicator);
        else
            TaskbarFlyouts.ShowCentered(VolumeMenu, VolumeIndicator);
    }

    private void QuickSettingsFlyout_Opening(object sender, object e) => QuickSettings.Opening(_quickSettingsPage);

    private void QuickSettingsFlyout_Closed(object sender, object e)
    {
        QuickSettings.Closed();
        _quickSettingsPage = QuickSettingsPage.Main;
    }

    private static void SetToolTip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text.Replace("\n", " "));
    }

    private void NetworkSettings_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenSettings(_owner.RunMode, "Network settings", "ms-settings:network", "ncpa.cpl");

    // As the shell: the Recording tab of Sound, as Control Panel has no microphone privacy page.
    private void Microphone_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenSettings(_owner.RunMode, "Microphone privacy settings", "ms-settings:privacy-microphone", "mmsys.cpl,,1");

    private void SoundSettings_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenSettings(_owner.RunMode, "Sound settings", "ms-settings:sound", "mmsys.cpl");

    // As the shell, the classic mixer: Settings can't start without Explorer.
    private void OpenVolumeMixer_Click(object sender, RoutedEventArgs e) =>
        Launcher.Launch(_owner.RunMode == RunMode.Shell
            ? new PinnedApp("Volume mixer", Path: "sndvol.exe")
            : new PinnedApp("Volume mixer", Path: "ms-settings:apps-volume"));

    private void Volume_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_indicators is null)
            return;

        _indicators.Volume = IndicatorDisplay.WheelVolume(_indicators.Volume, e.GetCurrentPoint(QuickSettingsButton).Properties.MouseWheelDelta);
        e.Handled = true;
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
        _trayPress = button.IsLeftButtonPressed ? new TrayPress(icon, element, Cursor.Position(), _tray.OverflowIcons.Contains(icon)) : null;
        e.Handled = true;
    }

    private void TrayIcon_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_tray is null || sender is not FrameworkElement { DataContext: TrayIcon icon } element)
            return;

        // Taken first: letting the pointer go raises PointerCaptureLost, which would end the drag unfinished.
        TrayPress? press = _trayPress;
        _trayPress = null;
        element.ReleasePointerCapture(e.Pointer);
        if (press is { Dragging: true })
        {
            // A drag is no click: the app hears nothing more.
            DropTrayIcon(press);
            e.Handled = true;
            return;
        }
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

        if (_trayPress is { } press && press.Icon == icon)
        {
            PointInt32 cursor = Cursor.Position();
            double threshold = TrayDragThreshold * Root.XamlRoot.RasterizationScale;
            if (!press.Dragging && (Math.Abs(cursor.X - press.Start.X) > threshold || Math.Abs(cursor.Y - press.Start.Y) > threshold))
                StartTrayDrag(press);
            if (press.Dragging)
            {
                DragTrayIcon(press, cursor);
                return;
            }
        }

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

    private void TrayIcon_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Let go over another window (the desktop, say), the icon gets no PointerReleased, only this: still a drop.
        // With the button still down, something else took the pointer, and the drag is off.
        if (_trayPress is { Dragging: true } press)
        {
            if (Cursor.IsButtonDown())
                EndTrayDrag(press);
            else
                DropTrayIcon(press);
        }
        _trayPress = null;
    }

    // Explorer's "Hidden icon menu" (Settings > Personalization > Taskbar), which Settings can't show without Explorer.
    private void HiddenIconMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tray is not null)
            _tray.ChevronVisible = HiddenIconMenuItem.IsChecked;
    }

    // Dragging tray icons between the taskbar and the overflow and along each, as in Explorer (SystemTray.dll's
    // DragDropManager): the icon stays in its place, dimmed, while a copy follows the pointer with a caption above it.
    private void StartTrayDrag(TrayPress press)
    {
        press.Dragging = true;
        EndTrayHover();
        if (press.Element is Grid { Children: [UIElement hover, UIElement image, ..] })
        {
            hover.Opacity = 0;
            image.Opacity = TrayDragPlaceholderOpacity;
        }
        TrayDragImage.Source = press.Icon.Icon;
        TrayDragPopup.IsOpen = true;
    }

    private void DragTrayIcon(TrayPress press, PointInt32 cursor)
    {
        TrayDropTarget? target = FindTrayDropTarget(cursor);
        TrayDragCaption caption = TrayIconOrder.Caption(press.FromOverflow, target);
        TrayDragCaptionBox.Visibility = caption == Tray.TrayDragCaption.None ? Visibility.Collapsed : Visibility.Visible;
        TrayDragGlyph.Glyph = caption switch
        {
            Tray.TrayDragCaption.Pin => "\uE718",
            Tray.TrayDragCaption.Unpin => "\uE77A",
            _ => "\uE733",
        };
        // The icon's bottom right at the pointer, the caption centred 12 above it (measured on Explorer's).
        Point point = ToRoot(cursor);
        TrayDragPopup.HorizontalOffset = point.X - 21;
        TrayDragPopup.VerticalOffset = point.Y - 58;

        RectInt32? beside = target is { IsChevron: false } on ? TrayIconRects(on.InOverflow)[on.Index] : null;
        TrayDropMarkerPopup.IsOpen = beside is not null;
        if (beside is { } rect)
        {
            // A line just left of the icon it goes before, or at the right edge of the one it goes after.
            Point marker = ToRoot(new PointInt32(target!.Value.After ? rect.X + rect.Width : rect.X - 1, rect.Y + rect.Height / 2));
            TrayDropMarkerPopup.HorizontalOffset = marker.X;
            TrayDropMarkerPopup.VerticalOffset = marker.Y - 18;
        }
    }

    private void DropTrayIcon(TrayPress press)
    {
        TrayDropTarget? target = FindTrayDropTarget(Cursor.Position());
        EndTrayDrag(press);
        if (target is { } drop && _tray!.Move(press.Icon, drop))
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => GrowTrayIcon(press.Icon));
    }

    private void EndTrayDrag(TrayPress press)
    {
        _trayPress = null;
        TrayDragPopup.IsOpen = false;
        TrayDropMarkerPopup.IsOpen = false;
        if (press.Element is Grid { Children: [_, UIElement image, ..] })
            image.Opacity = 1;
    }

    // A dropped icon grows into its new place, as Explorer's does.
    private void GrowTrayIcon(TrayIcon icon)
    {
        ItemsControl row = _tray!.PromotedIcons.Contains(icon) ? TrayIcons : OverflowIcons;
        if (row.ContainerFromItem(icon) is not ContentPresenter container || VisualTreeHelper.GetChildrenCount(container) == 0
            || VisualTreeHelper.GetChild(container, 0) is not Grid { Children: [_, UIElement image, ..] })
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(image);
        visual.CenterPoint = new Vector3(8, 8, 0);
        Vector3KeyFrameAnimation grow = visual.Compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0, 0, 1));
        grow.InsertKeyFrame(1, Vector3.One);
        grow.Duration = TrayDropGrowDuration;
        visual.StartAnimation("Scale", grow);
    }

    private TrayDropTarget? FindTrayDropTarget(PointInt32 cursor) => TrayIconOrder.Find(
        cursor,
        TrayIconRects(inOverflow: false),
        TaskbarFlyouts.WindowBounds(OverflowFlyout) is not null ? TrayIconRects(inOverflow: true) : null,
        OverflowButton.Visibility == Visibility.Visible ? BoundsOnScreen(OverflowButton) : null);

    private List<RectInt32> TrayIconRects(bool inOverflow)
    {
        ItemsControl row = inOverflow ? OverflowIcons : TrayIcons;
        RectInt32? overflow = inOverflow ? TaskbarFlyouts.WindowBounds(OverflowFlyout) : null;
        return [.. (inOverflow ? _tray!.OverflowIcons : _tray!.PromotedIcons).Select(icon => row.ContainerFromItem(icon) switch
        {
            FrameworkElement container when overflow is { } window => BoundsInPopup(container, window),
            FrameworkElement container => BoundsOnScreen(container),
            _ => default,
        })];
    }

    /// <summary>An element's place on screen, in pixels, in a popup window at <paramref name="window"/>.</summary>
    private RectInt32 BoundsInPopup(FrameworkElement element, RectInt32 window)
    {
        // The flyout's presenter fills the popup's window.
        UIElement top = element;
        while (top is not FlyoutPresenter && VisualTreeHelper.GetParent(top) is UIElement parent)
            top = parent;
        double scale = Root.XamlRoot.RasterizationScale;
        Rect rect = element.TransformToVisual(top).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            window.X + (int)(rect.X * scale), window.Y + (int)(rect.Y * scale), (int)(rect.Width * scale), (int)(rect.Height * scale));
    }

    /// <summary>A point on screen in the taskbar window's effective pixels.</summary>
    private Point ToRoot(PointInt32 point)
    {
        double scale = Root.XamlRoot.RasterizationScale;
        RectInt32 taskbar = _placement.Bounds;
        return new Point((point.X - taskbar.X) / scale, (point.Y - taskbar.Y) / scale);
    }

    private sealed class TrayPress(TrayIcon icon, FrameworkElement element, PointInt32 start, bool fromOverflow)
    {
        public TrayIcon Icon { get; } = icon;
        public FrameworkElement Element { get; } = element;
        public PointInt32 Start { get; } = start;
        public bool FromOverflow { get; } = fromOverflow;
        public bool Dragging { get; set; }
    }

    /// <summary>Rebuilds the task buttons from the pinned apps and the tracked windows.</summary>
    public void RefreshTasks()
    {
        // The drag measured the buttons as they were; windows that came or went are shown once it's over.
        if (_drag is not null)
        {
            _drag.RefreshPending = true;
            return;
        }

        ShellSettings settings = _owner.Settings.Current;
        IReadOnlyList<WindowInfo> windows = _owner.Tracker.Windows;
        double available = AvailableTaskWidth();
        // More buttons than fit are cut off rather than drawn over the clock.
        TaskList.MaxWidth = Math.Max(0, available);
        IReadOnlyList<TaskButtonModel> uncombined = TaskListBuilder.Build(settings.PinnedTaskbarApps, windows, combine: false);
        bool combine = TaskListBuilder.ShouldCombine(settings.CombineButtons, uncombined, available);
        IReadOnlyList<TaskButtonModel> models = TaskOrder.Arrange(
            combine ? TaskListBuilder.Build(settings.PinnedTaskbarApps, windows, combine: true) : uncombined,
            _owner.TaskOrder);
        _owner.TaskOrder = [.. models.Select(model => model.Key)];

        // Where each button is now, to slide it from there to its new place.
        bool animate = TaskList.IsLoaded;
        Dictionary<TaskButton, double> before = animate ? ButtonPositions() : [];

        // Update in place, so buttons keep their state and the list doesn't flicker.
        var keys = models.Select(model => model.Key).ToHashSet();
        for (int i = _tasks.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_tasks[i].Key))
                _tasks.RemoveAt(i);
        }
        var added = new List<TaskButton>();
        for (int i = 0; i < models.Count; i++)
        {
            int existing = IndexOf(models[i].Key, i);
            if (existing < 0)
            {
                _tasks.Insert(i, new TaskButton(models[i].Key));
                added.Add(_tasks[i]);
            }
            else if (existing != i)
            {
                _tasks.Move(existing, i);
            }
            _tasks[i].Update(models[i], _owner.Tracker, combine);
        }
        if (animate)
            AnimateLayoutChange(before, added);

        // The previews show windows that may have closed or opened.
        if (_thumbnails.Button is { } shown && !_tasks.Contains(shown))
            _thumbnails.Hide();
    }

    // The list's own item transitions show a moved button as removed and added again: it vanishes and reappears.
    // They're off (see the XAML), and changes are animated here instead: buttons that stay slide from where they
    // were, new ones grow in. Composition animations with explicit start values: an implicit transition starts from
    // whatever was last drawn, and the drag's Translation rules out a RenderTransform.
    private Dictionary<TaskButton, double> ButtonPositions()
    {
        var positions = new Dictionary<TaskButton, double>();
        foreach (TaskButton button in _tasks)
        {
            if (TaskList.ContainerFromItem(button) is UIElement container)
                positions[button] = container.TransformToVisual(Root).TransformPoint(default).X;
        }
        return positions;
    }

    private void AnimateLayoutChange(Dictionary<TaskButton, double> before, List<TaskButton> added)
    {
        // Running animations carry on; a button that moves again gets a new one in their place.
        long now = Environment.TickCount64;
        _layoutAnimations.RemoveAll(running => now - running.Started > s_layoutAnimationDuration.TotalMilliseconds);
        TaskList.UpdateLayout();

        Compositor compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        CompositionEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f));
        foreach (TaskButton button in _tasks)
        {
            if (TaskList.ContainerFromItem(button) is not FrameworkElement container)
                continue;

            if (added.Contains(button))
            {
                container.CenterPoint = new Vector3((float)container.ActualWidth / 2, (float)container.ActualHeight / 2, 0);
                Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(0, new Vector3(0.5f, 0.5f, 1));
                grow.InsertKeyFrame(1, Vector3.One, easing);
                Start(container, grow, nameof(UIElement.Scale));
                ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0, 0);
                fade.InsertKeyFrame(1, 1, easing);
                Start(container, fade, nameof(UIElement.Opacity));
            }
            else if (before.TryGetValue(button, out double was))
            {
                double moved = was - container.TransformToVisual(Root).TransformPoint(default).X;
                if (Math.Abs(moved) < 0.5)
                    continue;

                Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
                slide.InsertKeyFrame(0, new Vector3((float)moved, 0, 0));
                slide.InsertKeyFrame(1, Vector3.Zero, easing);
                Start(container, slide, nameof(UIElement.Translation));
            }
        }

        void Start(UIElement container, KeyFrameAnimation animation, string property)
        {
            animation.Target = property;
            animation.Duration = s_layoutAnimationDuration;
            container.StartAnimation(animation);
            _layoutAnimations.Add((container, property, Environment.TickCount64));
        }
    }

    // A drag measures the buttons where they belong. Stopping a composition animation would leave the value where it
    // had got to (a half-faded button), so running ones are finished instead: a last frame at their end value.
    private void FinishLayoutAnimations()
    {
        if (_layoutAnimations.Count == 0)
            return;

        Compositor compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        foreach ((UIElement container, string property, _) in _layoutAnimations)
        {
            KeyFrameAnimation end;
            if (property == nameof(UIElement.Opacity))
            {
                ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
                opacity.InsertKeyFrame(1, 1);
                end = opacity;
            }
            else
            {
                Vector3KeyFrameAnimation vector = compositor.CreateVector3KeyFrameAnimation();
                vector.InsertKeyFrame(1, property == nameof(UIElement.Scale) ? Vector3.One : Vector3.Zero);
                end = vector;
            }
            end.Target = property;
            end.Duration = TimeSpan.FromMilliseconds(1);
            container.StartAnimation(end);
        }
        _layoutAnimations.Clear();
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
        bool start = false;
        bool clock = false;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            start |= element == StartButton || element == SearchButton;
            clock |= element == Clock;
        }
        if (!start && e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed && InStartZone(e) && Root.CapturePointer(e.Pointer))
        {
            start = true;
            _startZonePointer = e.Pointer.PointerId;
            VisualStateManager.GoToState(StartButton, "Pressed", false);
            IconPress.Press((UIElement)StartButton.Content);
        }
        // Each toggles its own; the taskbar doesn't take the focus, so they wouldn't close by themselves.
        if (!start)
            _owner.HideStartMenu();
        if (!clock)
            _owner.HideClockFlyout();
    }

    // Pressed and released in the zone, it's a click on Start.
    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_startZonePointer != e.Pointer.PointerId)
            return;

        bool click = InStartZone(e);
        Root.ReleasePointerCapture(e.Pointer);
        EndStartZonePress();
        SetStartZoneHover(click);
        // Once the capture is gone: Start taking the foreground while the taskbar still has the mouse loses it
        // again, and closes.
        if (click)
            DispatcherQueue.TryEnqueue(() => _owner.ToggleStartMenu(this));
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_startZonePointer is null)
            SetStartZoneHover(InStartZone(e));
    }

    private void EndStartZonePress()
    {
        if (_startZonePointer is null)
            return;

        _startZonePointer = null;
        _startZoneHovered = false;
        VisualStateManager.GoToState(StartButton, "Normal", false);
        IconPress.Release((UIElement)StartButton.Content);
    }

    private void SetStartZoneHover(bool hovered)
    {
        if (hovered == _startZoneHovered || _startZonePointer is not null)
            return;

        _startZoneHovered = hovered;
        // Moving onto the button itself, the button shows its own hover.
        if (hovered || !StartButton.IsPointerOver)
            VisualStateManager.GoToState(StartButton, hovered ? "PointerOver" : "Normal", false);
    }

    // On the taskbar's own background around the Start button, not on the button (which handles itself).
    private bool InStartZone(PointerRoutedEventArgs e) => !StartButton.IsPointerOver && InStartZone(e.GetCurrentPoint(Root).Position);

    private bool InStartZone(Point point)
    {
        Rect button = StartButton.TransformToVisual(Root).TransformBounds(new Rect(0, 0, StartButton.ActualWidth, StartButton.ActualHeight));
        return point.Y >= 0 && point.Y < Root.ActualHeight
            && TaskbarLayout.IsStartZone(point.X, button.Left, button.Right, AppsPanel.HorizontalAlignment == HorizontalAlignment.Left);
    }

    private void TaskList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskButton button && !_suppressClick)
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
        if (sender is not FrameworkElement { DataContext: TaskButton button } element)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(element).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            Launcher.Launch(button.App);
            e.Handled = true;
        }
        else if (properties.IsRightButtonPressed)
        {
            _noPreviews = button;
            _hoverTimer.Stop();
        }
    }

    private bool CanPreview(TaskButton button) => !_taskMenuOpen && button != _noPreviews;

    private void TaskItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskButton button } element || _drag is not null)
            return;

        _hovered = (button, element);
        button.IsHovered = true;
        _hideTimer.Stop();
        if (!CanPreview(button) || _thumbnails.Button == button)
            return;
        // Once previews are open, they follow the pointer along the taskbar after a short pause, as in Explorer:
        // crossing a neighbour on the way up to the previews doesn't switch them.
        _hoverTimer.Interval = _thumbnails.Button is null ? s_previewDelay : s_previewSwitchDelay;
        _hoverTimer.Start();
    }

    private void TaskItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskButton button })
        {
            button.IsHovered = false;
            // The open menu takes the pointer; that isn't leaving the button.
            if (button == _noPreviews && !_taskMenuOpen)
                _noPreviews = null;
        }
        _hovered = null;
        // A right press isn't captured: let go of elsewhere, its release never comes here.
        ReleaseRightPress();
        _hoverTimer.Stop();
        _hideTimer.Start();
    }

    private void ShowThumbnails(TaskButton button, FrameworkElement element)
    {
        _thumbnails.Show(button, BoundsOnScreen(element), _monitor, _theme);
    }

    // Centred above the button rather than at the pointer, as Explorer shows a jump list.
    private void TaskItem_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is FrameworkElement element && FlyoutBase.GetAttachedFlyout(element) is { } menu)
        {
            e.Handled = true;
            TaskbarFlyouts.ShowCentered(menu, element);
        }
    }

    private void TaskMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        if (menu.Target?.DataContext is not TaskButton button)
            return;

        _taskMenuOpen = true;
        _hoverTimer.Stop();
        menu.SystemBackdrop = MenuBackdrop();
        _thumbnails.Hide();
        menu.Items.Clear();
        AddJumpList(menu, button.App);
        menu.Items.Add(MenuItem(button.App.DisplayName, "", "TaskLaunchMenuItem", () => Launcher.Launch(button.App)));
        menu.Items.Add(button.Pinned is { } pinned
            ? MenuItem("Unpin from taskbar", "", "TaskUnpinMenuItem", () => _owner.Unpin(pinned))
            : MenuItem("Pin to taskbar", "", "TaskPinMenuItem", () => _owner.Pin(button.App)));
        if (button.Windows.Count > 0)
        {
            // No separator above these: Explorer lists them straight after Pin to taskbar.
            IReadOnlyList<WindowInfo> windows = button.Windows;
            if (TaskEnding.IsOffered(button.App))
            {
                string? appId = button.App.AppUserModelId;
                menu.Items.Add(MenuItem(windows.Count == 1 ? "End task" : "End all tasks", "", "TaskEndTaskMenuItem",
                    () => TaskEnding.End(windows, appId)));
            }
            menu.Items.Add(MenuItem(windows.Count == 1 ? "Close window" : "Close all windows", "", "TaskCloseMenuItem", () =>
            {
                foreach (WindowInfo window in windows)
                    TopLevelWindows.Close(window.Handle);
            }));
        }
    }

    private void TaskMenu_Closed(object sender, object e)
    {
        _taskMenuOpen = false;
        // Closed with the pointer elsewhere, the button previews again on the next hover.
        if (_hovered?.Button != _noPreviews)
            _noPreviews = null;
    }

    // The app's jump list above the button's own items, as in Windows: its categories (Recent, the app's own), then
    // its tasks. Read each time the menu opens: apps change them whenever they like.
    private void AddJumpList(MenuFlyout menu, PinnedApp app)
    {
        string? appId = app.AppUserModelId ?? (app.Path is { } path ? JumpLists.ImplicitAppId(path) : null);
        if (appId is null)
            return;

        IReadOnlyList<JumpListCategory> categories;
        try
        {
            categories = JumpLists.Load(appId);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the jump list of {appId}", ex);
            return;
        }

        var headerStyle = (Style)Root.Resources["JumpListHeaderStyle"];
        int iconSize = (int)Math.Round(16 * Root.XamlRoot.RasterizationScale);
        foreach (JumpListCategory category in categories)
        {
            var header = new MenuFlyoutItem { Text = category.Title, Style = headerStyle };
            AutomationProperties.SetAutomationId(header, "JumpListHeader");
            menu.Items.Add(header);
            foreach (JumpListItem item in category.Items)
            {
                if (item.Kind == JumpListItemKind.Separator)
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    continue;
                }

                var icon = new ImageIcon();
                var entry = new MenuFlyoutItem { Text = item.Title, Icon = icon };
                AutomationProperties.SetAutomationId(entry, "JumpListItem");
                entry.Click += (_, _) => OpenJumpListItem(item);
                menu.Items.Add(entry);
                AppIcons.Load(() => JumpLists.GetIcon(item, iconSize), source => icon.Source = source);
            }
        }
        if (categories.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());
    }

    private void OpenJumpListItem(JumpListItem item)
    {
        try
        {
            JumpLists.Open(item, _hwnd);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open the jump list item {item.Title}", ex);
        }
    }

    // Reordering is done by hand rather than with the list's own drag and drop: that lifts the button off the
    // taskbar to follow the pointer anywhere, where Windows keeps it sliding along the row (see TaskReorder).
    private void TaskList_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressClick = false;
        PointerPointProperties properties = e.GetCurrentPoint(TaskList).Properties;
        if (_drag is not null || !(properties.IsLeftButtonPressed || properties.IsRightButtonPressed))
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null && element != TaskList; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem { Content: TaskButton button })
            {
                if (properties.IsLeftButtonPressed)
                    _pressed = (e.Pointer.PointerId, e.GetCurrentPoint(TaskList).Position.X, button);
                else
                    _rightPressed = button;
                if (TaskIcon(button) is { } icon)
                    IconPress.Press(icon);
                return;
            }
        }
    }

    private void TaskList_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed is not { } pressed || pressed.PointerId != e.Pointer.PointerId)
            return;

        double travel = e.GetCurrentPoint(TaskList).Position.X - pressed.X;
        if (_drag is null)
        {
            if (Math.Abs(travel) < TaskReorder.Threshold || !StartDrag(pressed.Button, e.Pointer))
                return;
        }

        TaskDrag drag = _drag!;
        double offset = TaskReorder.Offset(drag.Slots, drag.Index, travel);
        drag.Target = TaskReorder.TargetIndex(drag.Slots, drag.Index, offset);
        for (int i = 0; i < drag.Containers.Count; i++)
        {
            double x = i == drag.Index ? offset : TaskReorder.MakeWayOffset(drag.Slots, drag.Index, drag.Target, i);
            drag.Containers[i].Translation = new Vector3((float)x, 0, 0);
        }
        e.Handled = true;
    }

    private void TaskList_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleaseRightPress();
        if (_pressed is { } pressed && TaskIcon(pressed.Button) is { } icon)
            IconPress.Release(icon);
        _pressed = null;
        if (_drag is null)
            return;

        // The list would otherwise take the release as a click on the button.
        _suppressClick = true;
        EndDrag(drop: true);
        TaskList.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ReleaseRightPress()
    {
        if (_rightPressed is { } button && TaskIcon(button) is { } icon)
            IconPress.Release(icon);
        _rightPressed = null;
    }

    private void TaskList_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ReleaseRightPress();
        if (_pressed is { } pressed && TaskIcon(pressed.Button) is { } icon)
            IconPress.Release(icon);
        _pressed = null;
        if (_drag is not null)
            EndDrag(drop: false);
    }

    private bool StartDrag(TaskButton button, Pointer pointer)
    {
        // Measured where they belong, not where an animation is drawing them.
        FinishLayoutAnimations();
        int index = _tasks.IndexOf(button);
        var containers = new List<UIElement>();
        var slots = new List<(double Left, double Width)>();
        for (int i = 0; i < _tasks.Count; i++)
        {
            if (TaskList.ContainerFromIndex(i) is not FrameworkElement container)
                return false;
            containers.Add(container);
            slots.Add((container.TransformToVisual(TaskList).TransformPoint(default).X, container.ActualWidth));
        }
        if (index < 0 || !TaskList.CapturePointer(pointer))
            return false;

        _hoverTimer.Stop();
        _thumbnails.Hide();
        button.IsHovered = false;
        button.IsDragged = true;
        ScaleIcon(button, IconPress.Dragged);
        _hovered = null;
        // The dragged button follows the pointer straight away and passes over the others, which slide aside.
        foreach (UIElement container in containers)
            container.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        containers[index].TranslationTransition = null;
        Canvas.SetZIndex(containers[index], 1);
        _drag = new TaskDrag(containers, slots, index) { Target = index };
        return true;
    }

    private void EndDrag(bool drop)
    {
        TaskDrag drag = _drag!;
        _drag = null;
        TaskButton dragged = _tasks[drag.Index];
        dragged.IsDragged = false;
        ScaleIcon(dragged, 1);
        foreach (UIElement container in drag.Containers)
        {
            container.TranslationTransition = null;
            container.Translation = default;
        }
        Canvas.SetZIndex(drag.Containers[drag.Index], 0);
        if (drop && drag.Target != drag.Index)
        {
            // The buttons are already drawn where they belong, so the move shows no change.
            _tasks.Move(drag.Index, drag.Target);
            _owner.TaskOrder = [.. _tasks.Select(t => t.Key)];
            // Pinned apps keep their order for good; running ones for the session (TaskOrder).
            _owner.SetPinnedOrder([.. _tasks.Select(t => t.Pinned).OfType<PinnedApp>().DistinctBy(TaskGrouping.Key)]);
        }
        if (drag.RefreshPending)
            RefreshTasks();
    }

    private void ScaleIcon(TaskButton button, float scale)
    {
        if (TaskIcon(button) is { } icon)
            IconPress.Scale(icon, scale);
    }

    private UIElement? TaskIcon(TaskButton button) =>
        TaskList.ContainerFromItem(button) is ContentControl { ContentTemplateRoot: FrameworkElement root }
            ? root.FindName("TaskIcon") as UIElement
            : null;

    private sealed class TaskDrag(List<UIElement> containers, List<(double Left, double Width)> slots, int index)
    {
        public List<UIElement> Containers { get; } = containers;
        public List<(double Left, double Width)> Slots { get; } = slots;
        public int Index { get; } = index;
        public int Target { get; set; }
        public bool RefreshPending { get; set; }
    }

    // Drags from other apps and from the desktop, as over Explorer's taskbar (TaskbarDrop). WinUI's drop events, as
    // Explorer's XAML taskbar uses: the system draws the drag the same way, the dragged image small with the effect's
    // glyph and caption. Async void: everything is caught, as an exception would end the app.
    private async void Root_DragEnter(object sender, DragEventArgs e)
    {
        Reveal();
        _dragApp = null;
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            _dragApp = await AppToPinAsync(e.DataView);
            DragOverTaskbar(e);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read what was dragged over the taskbar", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The app the drag would pin: one program or shortcut to one, not pinned already.</summary>
    private async Task<PinnedApp?> AppToPinAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems))
            return null;

        IReadOnlyList<IStorageItem> items = await data.GetStorageItemsAsync();
        PinnedApp? app = TaskbarDrop.AppToPin([.. items.Select(item => item.Path)], ShellItems.ReadShortcut, ProgramName);
        return app is not null && !_owner.Settings.Current.PinnedTaskbarApps.Any(pinned => TaskGrouping.SameApp(pinned, app)) ? app : null;
    }

    private void DragOverTaskbar(DragEventArgs e)
    {
        Point point = e.GetPosition(Root);
        if (_dragApp is not null && e.AllowedOperations.HasFlag(DataPackageOperation.Link) && _drag is null)
        {
            // From Start to the tray, the buttons make way for it; over the tray and the clock it can't go.
            bool overTasks = point.X < RightPanel.TransformToVisual(Root).TransformPoint(default).X;
            ShowDropGap(overTasks ? point.X - TaskList.TransformToVisual(Root).TransformPoint(default).X : null);
            e.AcceptedOperation = overTasks ? DataPackageOperation.Link : DataPackageOperation.None;
            return;
        }

        // Nothing else can be dropped, but the window of the button under the drag comes forward to take it.
        SetDragHovered(ButtonAt(point));
        e.AcceptedOperation = DataPackageOperation.None;
    }

    private void EndDragOver()
    {
        ShowDropGap(null);
        SetDragHovered(null);
        _dragApp = null;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (_dragApp is { } app && _dropGap is { } gap)
        {
            (IReadOnlyList<string> order, IReadOnlyList<PinnedApp> pinned) =
                TaskbarDrop.Pin([.. _tasks.Select(task => (task.Key, task.Pinned))], app, gap.Index);
            // The buttons are drawn where they'll be, around the gap the app grows into.
            _dropGap = null;
            foreach (UIElement container in gap.Containers)
            {
                container.TranslationTransition = null;
                container.Translation = default;
            }
            _owner.TaskOrder = order;
            _owner.SetPinnedOrder(pinned);
            e.AcceptedOperation = DataPackageOperation.Link;
        }
        EndDragOver();
    }

    /// <summary>Opens a gap for the dragged app among the buttons at <paramref name="x"/> along the task list, or closes it.</summary>
    private void ShowDropGap(double? x)
    {
        if (x is not { } along)
        {
            if (_dropGap is { } open)
            {
                _dropGap = null;
                foreach (UIElement container in open.Containers)
                    container.Translation = default;
            }
            return;
        }

        if (_dropGap is null)
        {
            // Measured where they belong, not where an animation is drawing them.
            FinishLayoutAnimations();
            var containers = new List<UIElement>();
            var slots = new List<(double Left, double Width)>();
            for (int i = 0; i < _tasks.Count; i++)
            {
                if (TaskList.ContainerFromIndex(i) is not FrameworkElement container)
                    return;
                containers.Add(container);
                slots.Add((container.TransformToVisual(TaskList).TransformPoint(default).X, container.ActualWidth));
            }
            foreach (UIElement container in containers)
                container.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
            _dropGap = new DropGap(containers, slots);
        }

        DropGap gap = _dropGap;
        int index = TaskbarDrop.InsertionIndex(gap.Slots, along);
        if (index == gap.Index)
            return;
        gap.Index = index;
        for (int i = 0; i < gap.Containers.Count; i++)
            gap.Containers[i].Translation = new Vector3((float)TaskbarDrop.GapOffset(i, index, TaskListBuilder.CombinedButtonWidth), 0, 0);
    }

    // Pointer events don't come during a drag, so its hover is shown here.
    private void SetDragHovered(TaskButton? button)
    {
        if (button == _dragHovered)
            return;

        if (_dragHovered is { } previous)
            previous.IsHovered = false;
        _dragHovered = button;
        _dragHoverTimer.Stop();
        if (button is not null)
        {
            button.IsHovered = true;
            _dragHoverTimer.Start();
        }
        if (_thumbnails.Button is { } shown)
        {
            if (shown == button)
                _hideTimer.Stop();
            else
                _hideTimer.Start();
        }
    }

    // A window comes forward; several show their previews, and hovering one of those brings its window forward.
    private void DragHoverElapsed()
    {
        if (_dragHovered is not { } button || TaskList.ContainerFromItem(button) is not FrameworkElement element)
            return;

        if (button.Windows.Count == 1)
        {
            _thumbnails.Hide();
            // The drag's input went to its source, so this takes the foreground as Alt+Tab does.
            TopLevelWindows.SwitchTo(button.Windows[0].Handle);
        }
        else if (button.Windows.Count > 1 && _thumbnails.Button != button)
        {
            ShowThumbnails(button, element);
        }
    }

    private TaskButton? ButtonAt(Point point)
    {
        foreach (TaskButton button in _tasks)
        {
            if (TaskList.ContainerFromItem(button) is FrameworkElement container
                && container.TransformToVisual(Root).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight)).Contains(point))
                return button;
        }
        return null;
    }

    /// <summary>A program's name as Explorer gives it when pinned: its file description, else its file name.</summary>
    private static string ProgramName(string path)
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private sealed class DropGap(List<UIElement> containers, List<(double Left, double Width)> slots)
    {
        public List<UIElement> Containers { get; } = containers;
        public List<(double Left, double Width)> Slots { get; } = slots;
        public int Index { get; set; } = -1;
    }

    // From the keyboard, at the middle of the taskbar's top edge. Around Start, it's Start's menu.
    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        bool atPointer = e.TryGetPosition(Root, out Point pointer);
        if (atPointer && InStartZone(pointer))
            ShowQuickLinks();
        else
            TaskbarFlyouts.ShowAtPointer(TaskbarMenu, Root, atPointer ? pointer : new Point(Root.ActualWidth / 2, 0));
    }

    private void OverflowButton_Click(object sender, RoutedEventArgs e) => TaskbarFlyouts.ShowCentered(OverflowFlyout, OverflowButton);

    private void OverflowFlyout_Opening(object sender, object e) => TurnOverflowChevron(180);

    private void OverflowFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs e)
    {
        // A press on an icon on the taskbar, which may start dragging it into the overflow, leaves the overflow open,
        // as Explorer's (a click closes it when it's let go).
        if (_tray is not null && Cursor.IsButtonDown() && TrayIconRects(inOverflow: false).Any(rect => Contains(rect, Cursor.Position())))
        {
            e.Cancel = true;
            return;
        }
        TurnOverflowChevron(0);
    }

    private static bool Contains(RectInt32 rect, PointInt32 point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    private void TurnOverflowChevron(float degrees)
    {
        OverflowChevron.CenterPoint = new Vector3((float)OverflowChevron.ActualWidth / 2, (float)OverflowChevron.ActualHeight / 2, 0);
        OverflowChevron.RotationTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(200) };
        OverflowChevron.Rotation = degrees;
    }

    private void TaskbarMenu_Opening(object sender, object e)
    {
        ShellSettings settings = _owner.Settings.Current;
        AutoHideItem.IsChecked = settings.AutoHide;
        AlignCenterItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Center;
        AlignLeftItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Left;
        ShowSearchItem.IsChecked = settings.ShowSearchButton;
        ShowWidgetsItem.IsChecked = settings.ShowWidgetSidebar;
        CombineAlwaysItem.IsChecked = settings.CombineButtons == CombineButtons.Always;
        CombineWhenFullItem.IsChecked = settings.CombineButtons == CombineButtons.WhenFull;
        CombineNeverItem.IsChecked = settings.CombineButtons == CombineButtons.Never;
        AllDisplaysItem.IsChecked = settings.ShowOnAllDisplays;
        HiddenIconMenuItem.Visibility = _tray is not null ? Visibility.Visible : Visibility.Collapsed;
        HiddenIconMenuItem.IsChecked = _tray?.ChevronVisible == true;
        BackdropAcrylicItem.IsChecked = settings.TaskbarBackdrop == Backdrop.Acrylic;
        BackdropMicaItem.IsChecked = settings.TaskbarBackdrop == Backdrop.Mica;
        BackdropTranslucentItem.IsChecked = settings.TaskbarBackdrop == Backdrop.Translucent;
        BackdropTransparentItem.IsChecked = settings.TaskbarBackdrop == Backdrop.Transparent;
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e) => _owner.OpenTaskManager();

    private void Alignment_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            TaskbarAlignment = ReferenceEquals(sender, AlignLeftItem) ? TaskbarAlignment.Left : TaskbarAlignment.Center,
        });

    private void ShowSearch_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { ShowSearchButton = ShowSearchItem.IsChecked });

    private void ShowWidgets_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { ShowWidgetSidebar = ShowWidgetsItem.IsChecked });

    private void Combine_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            CombineButtons = ReferenceEquals(sender, CombineNeverItem) ? CombineButtons.Never
                : ReferenceEquals(sender, CombineWhenFullItem) ? CombineButtons.WhenFull
                : CombineButtons.Always,
        });

    private void Backdrop_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            TaskbarBackdrop = ReferenceEquals(sender, BackdropMicaItem) ? Backdrop.Mica
                : ReferenceEquals(sender, BackdropTranslucentItem) ? Backdrop.Translucent
                : ReferenceEquals(sender, BackdropTransparentItem) ? Backdrop.Transparent
                : Backdrop.Acrylic,
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
