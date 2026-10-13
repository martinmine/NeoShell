using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;
using AppBar = NeoShell.Interop.Windowing.AppBar;

namespace NeoShell.Taskbar;

/// <summary>
/// What makes a theme's window a taskbar, whatever it looks like: a frameless tool window that leaves the focus in the
/// app the user is working in, docked along the bottom of its monitor (an app bar above Explorer's taskbar, space
/// reserved from the work area as the shell, or nothing reserved when auto-hidden: it slides away and back), making way
/// for a full-screen app, and passing system broadcasts on to <see cref="Taskbars"/>.
/// </summary>
internal sealed class TaskbarDock
{
    // The window's content, kept: a closed window throws when asked for it, and it still hears of deactivation then.
    private readonly UIElement _content;
    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly nint _hwnd;
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private readonly AppBar? _appBar;
    private readonly Func<bool> _busy;
    private readonly Action _hiding;
    private readonly bool _autoHide;
    private readonly DispatcherQueueTimer? _autoHideTimer;
    private readonly DispatcherQueueTimer? _unhideTimer;
    private readonly DispatcherQueueTimer _slideTimer;
    private RectInt32 _shownBounds;
    private RectInt32 _slideFrom;
    private RectInt32 _slideTo;
    private int _slideStep;
    private bool _hidden;
    private int _pointerAwayTicks;
    private nint _fullScreenWindow;

    /// <param name="window">The taskbar's window, its content already created.</param>
    /// <param name="height">The taskbar's height in effective pixels.</param>
    /// <param name="busy">
    /// Whether something of the view's own keeps an auto-hidden taskbar up (Windows 11's thumbnails); the pointer on
    /// it, the keyboard in it, its open menus and flyouts, Start and the notification center always do.
    /// </param>
    /// <param name="hiding">An auto-hidden taskbar is about to slide away.</param>
    public TaskbarDock(Window window, Taskbars owner, DisplayMonitor monitor, ShellSettings settings, int height, Func<bool> busy, Action hiding)
    {
        _content = window.Content;
        _owner = owner;
        _monitor = monitor;
        _busy = busy;
        _hiding = hiding;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        window.AppWindow.SetPresenter(presenter);

        nint hwnd = _hwnd = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        // Out of Alt+Tab, and clicking it leaves the focus in the app the user is working in.
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        // Win+T and the flyouts make the taskbar activatable for a while (see TakeKeyboard, TaskbarFlyouts); once it's
        // left, clicks go back to leaving the focus alone, and its flyouts close, as Explorer's do.
        window.Activated += (_, e) =>
        {
            IsActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (IsActive)
                return;
            WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.NoActivate);
            if (_content.XamlRoot is { } root)
                TaskbarFlyouts.HideAll(root);
        };
        // Alt+F4 while it has the keyboard (Win+T) would close it; Explorer's taskbar asks to shut down instead.
        window.AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            owner.ShowShutDownDialog();
        };
        _frameless = new FramelessWindow(hwnd);
        Peek.Exclude(hwnd);
        _messages = new WindowSubclass(hwnd, OnMessage);

        _autoHide = settings.AutoHide;
        RectInt32 bounds = TaskbarLayout.Bounds(monitor.Bounds, monitor.Dpi, height);
        if (_autoHide)
        {
            // Nothing is reserved: windows get the whole screen and the taskbar slides over them. Alongside, it sits
            // above Explorer's taskbar, at the bottom of the work area.
            RectInt32 area = owner.RunMode == RunMode.AlongsideExplorer ? monitor.WorkArea : monitor.Bounds;
            bounds = TaskbarLayout.Bounds(area, monitor.Dpi, height);
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
        _placement = new PinnedWindow(hwnd, bounds, PinnedLayer.Topmost) { MovesInPlace = true };

        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _slideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(16), SlideStep);
        _slideTimer.IsRepeating = true;
        if (_autoHide)
        {
            _autoHideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(250), CheckAutoHide);
            _autoHideTimer.IsRepeating = true;
            _autoHideTimer.Start();
            // The pointer at the sliver left on screen brings it back, whatever window is over the sliver (a topmost
            // one, or a maximized window's invisible border): Explorer's taskbar checks where the pointer is, every
            // 50 ms while hidden (Taskbar.dll's unhide timer), rather than waiting for it to enter its window.
            _unhideTimer = CreateTimer(dispatcher, TimeSpan.FromMilliseconds(50), CheckUnhide);
            _unhideTimer.IsRepeating = true;
        }

        window.Closed += (_, _) =>
        {
            _autoHideTimer?.Stop();
            _unhideTimer?.Stop();
            _slideTimer.Stop();
            // Give the space back first, so windows can use it straight away.
            if (_appBar is not null)
                _appBar.Dispose();
            else if (!_autoHide)
                ShellWorkArea.ReserveBottom(monitor.Bounds, 0);
        };
    }

    public nint Handle => _hwnd;

    /// <summary>Where the taskbar is on screen when shown (an auto-hidden one may be slid away), in pixels.</summary>
    public RectInt32 ScreenBounds => _shownBounds;

    /// <summary>Whether the taskbar has the keyboard (Win+T, or a flyout that took it).</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Lets go of the window before it closes, as widget windows do: kept from WinUI's moves while closing (see
    /// <see cref="WindowClosing.IgnoreMoves"/>). The view closes its menus and flyouts first, and the window after.
    /// </summary>
    public void Shut()
    {
        _placement.Dispose();
        _messages.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
    }

    /// <summary>Slides an auto-hidden taskbar back into view.</summary>
    public void Reveal()
    {
        _pointerAwayTicks = 0;
        if (!_hidden)
            return;

        _hidden = false;
        _unhideTimer?.Stop();
        SlideTo(_shownBounds);
    }

    // As Explorer's: not over a full-screen app, which the taskbar makes way for, nor over a topmost window that fills
    // the monitor (a maximized one, with nothing reserved), though over any other window.
    private void CheckUnhide()
    {
        if (TaskbarLayout.RevealsAt(Cursor.Position(), _shownBounds) && _fullScreenWindow == 0
            && !TopLevelWindows.IsTopmostWindowCovering(_monitor.Bounds))
        {
            Reveal();
        }
    }

    // Auto-hide: slide away once the pointer has been off the taskbar for a moment and nothing of it is in use.
    private void CheckAutoHide()
    {
        // No XamlRoot until the content's first layout, which may come after the first tick when the shell is starting:
        // asking WinUI for the popups of none throws, and an exception in a timer's tick ends NeoShell.
        if (_hidden || _content.XamlRoot is not { } xamlRoot)
            return;

        RectInt32 shown = _shownBounds;
        PointInt32 pointer = Cursor.Position();
        bool pointerOver = pointer.X >= shown.X && pointer.X < shown.X + shown.Width
            && pointer.Y >= shown.Y && pointer.Y < shown.Y + shown.Height;
        bool inUse = pointerOver
            || IsActive // Win+T
            || VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count > 0 // menus and flyouts
            || _busy()
            || _owner.IsStartMenuOpen
            || _owner.IsClockFlyoutOpen;
        _pointerAwayTicks = inUse ? 0 : _pointerAwayTicks + 1;
        if (_pointerAwayTicks >= 3)
        {
            _hidden = true;
            _hiding();
            SlideTo(TaskbarLayout.HiddenBounds(shown));
            _unhideTimer?.Start();
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
    /// Activates the taskbar for the keyboard. A no-activate window can't become active, and only the active window
    /// gets the keyboard; once it's left, the taskbar is no-activate again.
    /// </summary>
    public void TakeKeyboard()
    {
        WindowStyles.RemoveExtended(_hwnd, ExtendedWindowStyles.NoActivate);
        TopLevelWindows.Activate(_hwnd);
    }

    /// <summary>An element's place on screen, in pixels.</summary>
    public RectInt32 BoundsOnScreen(FrameworkElement element)
    {
        double scale = _content.XamlRoot.RasterizationScale;
        Rect rect = element.TransformToVisual(_content).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        RectInt32 taskbar = _placement.Bounds;
        return new RectInt32(
            taskbar.X + (int)(rect.X * scale), taskbar.Y + (int)(rect.Y * scale), (int)(rect.Width * scale), (int)(rect.Height * scale));
    }

    /// <summary>A point on screen in the taskbar window's effective pixels.</summary>
    public Point FromScreen(PointInt32 point)
    {
        double scale = _content.XamlRoot.RasterizationScale;
        RectInt32 taskbar = _placement.Bounds;
        return new Point((point.X - taskbar.X) / scale, (point.Y - taskbar.Y) / scale);
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

    private static DispatcherQueueTimer CreateTimer(DispatcherQueue dispatcher, TimeSpan interval, Action onTick)
    {
        DispatcherQueueTimer timer = dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => onTick();
        return timer;
    }
}
