using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Desktop;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Notifications;
using NeoShell.QuickSettings;
using NeoShell.Interop.Tray;
using NeoShell.Settings;
using NeoShell.Snap;
using NeoShell.Tray;
using NeoShell.StartMenu;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Taskbar;

/// <summary>
/// The taskbars: one per monitor (or only on the primary one), recreated when displays, DPI or settings change.
/// </summary>
internal sealed class Taskbars : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Action _exit;
    private readonly Action _switchToExplorer;
    private readonly ShowDesktop _showDesktop = new();
    private readonly List<TaskbarWindow> _windows = [];
    // Explorer follows its clock settings as Settings writes them, with no message.
    private readonly RegistryWatcher _clockSettingsWatcher = new(ClockSettings.ExplorerAdvancedKey);
    private readonly RegistryWatcher _additionalClocksWatcher = new(ClockSettings.AdditionalClocksKey, subtree: true);
    private readonly RegistryWatcher _searchWatcher = new(TaskbarSearch.KeyPath);
    private ElementTheme _theme = SystemTheme.Read();
    private Color? _accent = SystemTheme.ReadAccent();
    private ShellSettings _windowSettings;
    private StartMenuWindow? _startMenu;
    private FocusSession? _focus;
    private ClockFlyout? _clockFlyout;
    // The taskbar the notification center and calendar last opened on: its clock keeps its plate while they're open.
    private TaskbarWindow? _clockFlyoutTaskbar;
    private ToastPopups? _toasts;
    // Other apps' app bars, served with the tray.
    private AppBars? _appBars;
    private long _lastKeyboardToggle;
    private bool _updateQueued;
    private bool _recreate;
    private bool _disposed;

    public Taskbars(RunMode runMode, SettingsStore settings, Action exit, Action switchToExplorer)
    {
        RunMode = runMode;
        Settings = settings;
        _exit = exit;
        _switchToExplorer = switchToExplorer;
        _windowSettings = settings.Current;
        // Start shows icons at up to 32 effective pixels, the taskbar at 24; load them sharp for the densest monitor.
        // The taskbar gets its own size, not a scaled-down 32: packaged apps have their own image for 24 pixels.
        uint dpi = DisplayMonitor.GetAll().Select(m => m.Dpi).DefaultIfEmpty(96u).Max();
        Icons = new AppIcons((int)Math.Round(32 * dpi / 96.0));
        Tracker = new WindowTracker(new AppIcons((int)Math.Round(24 * dpi / 96.0)), announceButtons: runMode == RunMode.Shell);
        if (runMode == RunMode.Shell)
            Tracker.ButtonBounds = hwnd => _windows.Select(w => w.TaskButtonBounds(hwnd)).FirstOrDefault(b => b is not null);
        Tracker.Changed += RefreshTasks;
        Tracker.ForegroundChanged += UpdateFullScreen;
        Settings.Changed += OnSettingsChanged;
        _clockSettingsWatcher.Changed += () => _dispatcher.TryEnqueue(UpdateClocks);
        _additionalClocksWatcher.Changed += () => _dispatcher.TryEnqueue(UpdateClocks);
        _searchWatcher.Changed += () => _dispatcher.TryEnqueue(UpdateSearch);
        // The former toggle: hidden carries over to Explorer's setting, which replaces it.
        if (settings.Current.ShowSearchButton is { } showSearch)
        {
            if (!showSearch)
                TaskbarSearch.Save(TaskbarSearchMode.Hidden);
            settings.Update(settings.Current with { ShowSearchButton = null });
        }
        SearchMode = TaskbarSearch.Read();
    }

    public RunMode RunMode { get; }

    /// <summary>The taskbar's theme, which its panels and the window switcher share.</summary>
    public ElementTheme Theme => _theme;

    /// <summary>The taskbar's colour when Windows shows the accent colour on Start and taskbar, otherwise null.</summary>
    public Color? Accent => _accent;

    /// <summary>
    /// The taskbars were updated after a change of theme, accent colour, displays or their settings; their space on
    /// screen may have changed.
    /// </summary>
    public event Action? Updated;

    /// <summary>A device was plugged in or removed (comes in bursts).</summary>
    public event Action? DevicesChanged;

    public SettingsStore Settings { get; }

    /// <summary>What the clocks show: seconds, the time and date at all, the notification bell, other time zones.</summary>
    public ClockSettings ClockSettings { get; private set; } = ClockSettings.Read();

    /// <summary>How the search entry point shows, Explorer's setting; read after the former toggle is carried over.</summary>
    public TaskbarSearchMode SearchMode { get; private set; }

    public AppIcons Icons { get; }

    public WindowTracker Tracker { get; }

    /// <summary>Snap as the shell, whose snap groups the previews show; null alongside Explorer.</summary>
    public WindowSnapping? Snapping { get; set; }

    /// <summary>
    /// Button keys in the order last shown, shared by the taskbars and kept when they're recreated (see
    /// <see cref="Taskbar.TaskOrder"/>).
    /// </summary>
    public IReadOnlyList<string> TaskOrder { get; set; } = [];

    /// <summary>Network, volume and microphone state for the primary taskbar's indicators.</summary>
    public Indicators? Indicators { get; private set; }

    /// <summary>The notifications Windows keeps, and Do not disturb.</summary>
    public NotificationCenter? Notifications { get; private set; }

    /// <summary>The system tray; only when NeoShell is the shell and no other tray is running.</summary>
    public NotificationArea? Tray { get; private set; }

    /// <summary>Toasts, which NeoShell shows only as the shell.</summary>
    public ToastPopups? Toasts => _toasts;

    public void Show()
    {
        Tracker.Start();
        Indicators = new Indicators();
        if (RunMode == RunMode.Shell && !TrayHost.IsTrayRunning())
        {
            Tray = new NotificationArea(showAll: Settings.Current.TrayMode == TrayMode.ShowAll);
            if (Settings.Current.TrayMode is not null)
                Settings.Update(Settings.Current with { TrayMode = null });
            Tray.IconBounds = icon => PrimaryWindow?.TrayIconBounds(icon);
            Tray.TaskbarListCalled += Tracker.Apply;
            Tray.ThumbBarCalled += Tracker.Apply;
            Tray.WindowShaken += OnWindowShaken;
            Tray.BalloonRequested += ShowBalloon;
            Tray.BalloonWithdrawn += key => _toasts?.Hide(key);
            _appBars = new AppBars(Tray, () => PrimaryWindow?.ScreenBounds, () => _windowSettings.AutoHide);
        }
        Notifications = new NotificationCenter();
        // Explorer shows toasts itself while it runs.
        if (RunMode == RunMode.Shell)
        {
            _toasts = new ToastPopups(
                Notifications,
                RunMode,
                () => PrimaryWindow is { } window ? (window.Monitor, window.ScreenBounds) : null,
                () => (_theme, _accent),
                () => IsClockFlyoutOpen);
        }
        _recreate = true;
        Update();
        // Start, the clock's flyout and Quick Settings take over half a second to build: the taskbar shows first.
        // They're made up front still, so they open instantly (and Start's app catalog is loaded), unless clicked
        // before they're made.
        UiThread.AfterFramesDrawn(() =>
        {
            if (_disposed)
                return;
            _ = StartMenu;
            _ = ClockFlyout;
            PrimaryWindow?.PrepareQuickSettings();
            Notifications.Start();
        });
    }

    private StartMenuWindow StartMenu => _startMenu ??= new StartMenuWindow(this);

    private ClockFlyout ClockFlyout
    {
        get
        {
            if (_clockFlyout is null)
            {
                _focus = new FocusSession(Notifications!, asShell: RunMode == RunMode.Shell);
                _clockFlyout = new ClockFlyout(Notifications!, _focus, Settings, RunMode);
                _clockFlyout.OpenChanged += ShowClockOpen;
            }
            return _clockFlyout;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Settings.Changed -= OnSettingsChanged;
        _clockSettingsWatcher.Dispose();
        _additionalClocksWatcher.Dispose();
        _searchWatcher.Dispose();
        Tracker.Dispose();
        _appBars?.Dispose();
        Tray?.Dispose();
        Indicators?.Dispose();
        _startMenu?.Close();
        _toasts?.Dispose();
        _clockFlyout?.Dispose();
        _focus?.Dispose();
        Notifications?.Dispose();
        CloseWindows();
    }

    // A balloon that can't show times out at once, as Explorer's.
    private void ShowBalloon(TrayBalloon balloon)
    {
        if (_toasts?.ShowBalloon(balloon, balloonEvent => Tray?.Send(balloon, balloonEvent)) != true)
            Tray?.Send(balloon, BalloonEvent.TimedOut);
    }

    /// <summary>Opens Start above <paramref name="taskbar"/>, or closes it if it's open.</summary>
    public void ToggleStartMenu(TaskbarWindow taskbar)
    {
        StartMenuWindow startMenu = StartMenu;
        if (startMenu.IsOpen)
        {
            startMenu.Hide();
        }
        else if (!startMenu.WasJustDeactivated)
        {
            taskbar.Reveal();
            startMenu.Show(taskbar.Monitor, taskbar.ScreenBounds, taskbar.Handle, Settings.Current.TaskbarAlignment == TaskbarAlignment.Center, _theme, _accent);
        }
    }

    public bool IsStartMenuOpen => _startMenu?.IsOpen == true;

    /// <summary>Win+1…9: the Nth button on the primary taskbar (0-based here).</summary>
    public void ActivateTask(int index) => PrimaryWindow?.ActivateTask(index);

    /// <summary>Win+Shift+1…9, and Win+Ctrl+Shift+1…9 as administrator: another instance of the Nth button's app.</summary>
    public void LaunchTask(int index, bool elevated) => PrimaryWindow?.LaunchTask(index, elevated);

    /// <summary>Win+Ctrl+1…9: the Nth button's last active window.</summary>
    public void ActivateLastWindow(int index) => PrimaryWindow?.ActivateLastWindow(index);

    /// <summary>Win+Alt+1…9: the Nth button's jump list.</summary>
    public void ShowJumpList(int index) => PrimaryWindow?.ShowJumpList(index);

    public void HideStartMenu() => _startMenu?.Hide();

    /// <summary>
    /// The Windows key or Ctrl+Esc. Windows may report the same press twice (the hook, and the task list request to
    /// the shell window), so a second toggle right after the first is ignored.
    /// </summary>
    public void ToggleStartMenuFromKeyboard()
    {
        long now = Environment.TickCount64;
        if (now - _lastKeyboardToggle < 300)
            return;
        _lastKeyboardToggle = now;

        if (PrimaryWindow is { } window)
            ToggleStartMenu(window);
    }

    /// <summary>Win+S: opens Start, whose search box has the focus.</summary>
    public void OpenStartMenu()
    {
        if (!IsStartMenuOpen && PrimaryWindow is { } window)
            ToggleStartMenu(window);
    }

    /// <summary>The Copilot key set to search: Start with its search box, or closed if it's open (Explorer's search toggles).</summary>
    public void ToggleStartSearch()
    {
        if (IsStartMenuOpen)
            HideStartMenu();
        else
            OpenStartMenu();
    }

    /// <summary>Win+A, Win+Ctrl+V, Win+K, Win+P: Quick Settings on the primary taskbar, open on the page.</summary>
    public void ShowQuickSettings(QuickSettingsPage page) => PrimaryWindow?.ShowQuickSettings(page);

    /// <summary>Win+Space's input switcher, on the primary taskbar's input indicator.</summary>
    public void RunInputSwitch(InputSwitchCommand command) => PrimaryWindow?.RunInputSwitch(command);

    /// <summary>Win+X: Start's Quick Link menu on the primary taskbar.</summary>
    public void ToggleQuickLinks() => PrimaryWindow?.ToggleQuickLinks();

    /// <summary>Opens the notification center and calendar at the right of <paramref name="taskbar"/>, or closes them.</summary>
    public void ToggleClockFlyout(TaskbarWindow taskbar)
    {
        ClockFlyout clockFlyout = ClockFlyout;
        if (clockFlyout.IsOpen)
        {
            clockFlyout.Hide();
        }
        else if (!clockFlyout.WasJustDeactivated)
        {
            taskbar.Reveal();
            _clockFlyoutTaskbar = taskbar;
            clockFlyout.Show(taskbar.Monitor, taskbar.ScreenBounds, _theme, _accent);
        }
    }

    private void ShowClockOpen()
    {
        foreach (TaskbarWindow window in _windows)
            window.ShowClockOpen(_clockFlyout?.IsOpen == true && window == _clockFlyoutTaskbar);
    }

    /// <summary>Win+N: the notification center on the primary taskbar's monitor.</summary>
    public void ToggleNotificationCenter()
    {
        if (PrimaryWindow is { } window)
            ToggleClockFlyout(window);
    }

    public bool IsClockFlyoutOpen => _clockFlyout?.IsOpen == true;

    public void HideClockFlyout() => _clockFlyout?.Hide();

    /// <summary>Win+T (Win+Shift+T from the end): puts the keyboard focus on the primary taskbar's buttons.</summary>
    public void FocusTaskbar(bool last = false)
    {
        PrimaryWindow?.Reveal();
        PrimaryWindow?.FocusTaskList(last);
    }

    /// <summary>Win+B: puts the keyboard focus on the primary taskbar's notification area.</summary>
    public void FocusTray()
    {
        PrimaryWindow?.Reveal();
        PrimaryWindow?.FocusTray();
    }

    /// <summary>Win+R: Windows' Run dialog above the primary taskbar's left end, where the Quick Link menu puts it.</summary>
    public void ShowRunDialog()
    {
        if (PrimaryWindow is { } window)
            ShellLaunch.ShowRunDialog(window.ScreenBounds.X, window.ScreenBounds.Y);
    }

    /// <summary>Win+Alt+K: mutes or unmutes the microphone; the microphone indicator shows it while an app records.</summary>
    public void ToggleMicrophoneMute() => Indicators?.ToggleMicrophoneMute();

    /// <summary>
    /// Win+Comma: while <paramref name="on"/>, every window is hidden but the desktop and the taskbars (Aero Peek
    /// at a window that's left out of peeking anyway).
    /// </summary>
    public void PeekAtDesktop(bool on)
    {
        if (PrimaryWindow is not { } window)
            return;

        if (on)
            Peek.Show(window.Handle, window.Handle);
        else
            Peek.End(window.Handle);
    }

    /// <summary>Alt+F4 on the desktop or the taskbar: the Shut Down Windows dialog.</summary>
    public void ShowShutDownDialog() => ShutDownDialog.Open(_theme);

    /// <summary>The primary taskbar's window, for what needs a window of NeoShell's (the clipboard); 0 when none.</summary>
    public nint PrimaryHandle => PrimaryWindow?.Handle ?? 0;

    private TaskbarWindow? PrimaryWindow => _windows.FirstOrDefault(window => window.Monitor.IsPrimary) ?? _windows.FirstOrDefault();

    public void SwitchToExplorer() => _switchToExplorer();

    public void Pin(PinnedApp app)
    {
        if (!Settings.Current.PinnedTaskbarApps.Any(p => TaskGrouping.SameApp(p, app)))
            SetPinnedOrder([.. Settings.Current.PinnedTaskbarApps, app]);
    }

    public void Unpin(PinnedApp app) =>
        SetPinnedOrder([.. Settings.Current.PinnedTaskbarApps.Where(p => !TaskGrouping.SameApp(p, app))]);

    public void SetPinnedOrder(IReadOnlyList<PinnedApp> pinned)
    {
        if (!pinned.SequenceEqual(Settings.Current.PinnedTaskbarApps))
            Settings.Update(Settings.Current with { PinnedTaskbarApps = pinned });
    }

    public void QueueRecreate()
    {
        _recreate = true;
        QueueUpdate();
    }

    /// <summary>System broadcasts reach every taskbar window; they're handled once, after the burst.</summary>
    public void OnBroadcast(uint message)
    {
        switch (message)
        {
            case WindowMessages.DisplayChange:
                QueueRecreate();
                break;
            case WindowMessages.SettingChange: // theme, accent colour, regional formats
            case WindowMessages.TimeChange:
                QueueUpdate();
                break;
            case WindowMessages.DeviceChange:
                DevicesChanged?.Invoke();
                break;
        }
    }

    public void ToggleDesktop() => _showDesktop.Toggle();

    /// <summary>Win+M.</summary>
    public void MinimizeAll() => _showDesktop.MinimizeAll();

    /// <summary>Win+Shift+M.</summary>
    public void RestoreMinimized() => _showDesktop.Restore();

    /// <summary>Win+Home: every window but the one in front; nothing when that's the desktop or the taskbar.</summary>
    public void ToggleAllButForeground()
    {
        nint foreground = TopLevelWindows.GetForeground();
        if (TopLevelWindows.IsShakable(foreground))
            _showDesktop.ToggleAllBut(foreground);
    }

    // Shaking a window's title bar is Win+Home for that window, when the setting allows it.
    private void OnWindowShaken(nint window)
    {
        if (ShowDesktop.ShakingAllowed())
            _showDesktop.ToggleAllBut(window);
    }

    public void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // E.g. the elevation prompt was cancelled.
            Log.Warn("Could not start Task Manager", ex);
        }
    }

    public void Exit() => _exit();

    private void OnSettingsChanged()
    {
        // Where the taskbars go and how they're laid out needs new windows; the backdrop and buttons change in place.
        ShellSettings current = Settings.Current;
        if (current.TaskbarAlignment != _windowSettings.TaskbarAlignment
            || current.ShowOnAllDisplays != _windowSettings.ShowOnAllDisplays
            || current.AutoHide != _windowSettings.AutoHide)
        {
            QueueRecreate();
        }
        else
        {
            foreach (TaskbarWindow window in _windows)
                window.SetBackdrop(current.TaskbarBackdrop);
            RefreshTasks();
        }
    }

    private void RefreshTasks()
    {
        foreach (TaskbarWindow window in _windows)
        {
            window.RefreshTasks();
            window.RefreshTray();
        }
    }

    /// <summary>
    /// A full-screen app in front pushes its monitor's taskbar out of the topmost band, just below it; anything else
    /// in front brings the taskbar back on top.
    /// </summary>
    private void UpdateFullScreen()
    {
        nint foreground = Tracker.Foreground;
        bool candidate = foreground != 0
            && TopLevelWindows.Exists(foreground)
            && !TopLevelWindows.IsMinimized(foreground)
            // Maximized fills the screen too when nothing reserves space (auto-hide), but isn't full screen.
            && !TopLevelWindows.IsMaximized(foreground)
            && TopLevelWindows.GetProcessId(foreground) != Environment.ProcessId
            && !TopLevelWindows.IsDesktop(foreground);
        nint monitor = candidate ? TopLevelWindows.MonitorOf(foreground) : 0;
        RectInt32 bounds = candidate ? TopLevelWindows.GetBounds(foreground) : default;

        foreach (TaskbarWindow window in _windows)
        {
            bool fullScreen = candidate && window.Monitor.Handle == monitor
                && (Tracker.IsMarkedFullScreen(foreground) || TaskbarLayout.IsFullScreen(bounds, window.Monitor.Bounds));
            window.SetFullScreenWindow(fullScreen ? foreground : 0);
        }
        // App bars hear of it on every monitor, taskbar or not.
        if (_appBars is not null)
        {
            bool fullScreen = candidate && (Tracker.IsMarkedFullScreen(foreground)
                || DisplayMonitor.GetAll().Any(m => m.Handle == monitor && TaskbarLayout.IsFullScreen(bounds, m.Bounds)));
            _appBars.SetFullScreenMonitor(fullScreen ? monitor : 0);
        }
    }

    private void UpdateClocks()
    {
        ClockSettings = ClockSettings.Read();
        foreach (TaskbarWindow window in _windows)
            window.UpdateClock();
    }

    // The key holds many other values of Windows Search's, written as it works.
    private void UpdateSearch()
    {
        TaskbarSearchMode mode = TaskbarSearch.Read();
        if (mode == SearchMode)
            return;

        SearchMode = mode;
        foreach (TaskbarWindow window in _windows)
            window.SetSearchMode(mode);
    }

    /// <summary>The taskbar menu's choice, saved where Explorer keeps it.</summary>
    public void SetSearchMode(TaskbarSearchMode mode)
    {
        TaskbarSearch.Save(mode);
        UpdateSearch();
    }

    private void QueueUpdate()
    {
        if (_updateQueued)
            return;

        _updateQueued = true;
        _dispatcher.Post(Update);
    }

    private void Update()
    {
        _updateQueued = false;
        try
        {
            // Pick up changed regional formats and time zone for the clock.
            CultureInfo.CurrentCulture.ClearCachedData();
            TimeZoneInfo.ClearCachedData();
            _theme = SystemTheme.Read();
            _accent = SystemTheme.ReadAccent();
            ClockSettings = ClockSettings.Read();

            if (_recreate)
            {
                _recreate = false;
                CreateWindows();
            }
            else
            {
                foreach (TaskbarWindow window in _windows)
                {
                    window.SetTheme(_theme, _accent);
                    window.UpdateClock();
                }
            }
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Updating the taskbars failed", ex);
        }
    }

    private void CreateWindows()
    {
        CloseWindows();

        bool autoHideChanged = Settings.Current.AutoHide != _windowSettings.AutoHide;
        _windowSettings = Settings.Current;
        IReadOnlyList<DisplayMonitor> monitors =
            TaskbarLayout.MonitorsWithTaskbar(DisplayMonitor.GetAll(), _windowSettings.ShowOnAllDisplays);
        foreach (DisplayMonitor monitor in monitors)
        {
            var window = new TaskbarWindow(this, monitor, _windowSettings, _theme, _accent);
            window.RefreshTasks();
            window.AppWindow.Show(activateWindow: false);
            _windows.Add(window);
            if (monitor.IsPrimary)
                Tray?.SetTaskbarBounds(window.ScreenBounds);
        }
        Log.Info($"Taskbars on {monitors.Count} monitor(s)");
        // Windows gives every monitor its whole screen back when the displays change. The space goes out again only
        // now that the new taskbars took theirs: app bars told of the change place themselves around them.
        ShellWorkArea.SendAgain();
        UpdateFullScreen();
        if (autoHideChanged)
            _appBars?.NotifyStateChange();
    }

    private void CloseWindows()
    {
        foreach (TaskbarWindow window in _windows)
            window.Shut();
        _windows.Clear();
    }
}
