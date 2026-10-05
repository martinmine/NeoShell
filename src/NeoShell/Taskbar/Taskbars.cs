using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Notifications;
using NeoShell.QuickSettings;
using NeoShell.Interop.Tray;
using NeoShell.Settings;
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
    private ElementTheme _theme = SystemTheme.Read();
    private Color? _accent = SystemTheme.ReadAccent();
    private ShellSettings _windowSettings;
    private StartMenuWindow? _startMenu;
    private FocusSession? _focus;
    private ClockFlyout? _clockFlyout;
    private ToastPopups? _toasts;
    private long _lastKeyboardToggle;
    private bool _updateQueued;
    private bool _recreate;

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
        Tracker.Changed += RefreshTasks;
        Tracker.ForegroundChanged += UpdateFullScreen;
        Settings.Changed += OnSettingsChanged;
    }

    public RunMode RunMode { get; }

    public SettingsStore Settings { get; }

    public AppIcons Icons { get; }

    public WindowTracker Tracker { get; }

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

    public void Show()
    {
        Tracker.Start();
        Indicators = new Indicators();
        if (RunMode == RunMode.Shell && !TrayHost.IsTrayRunning())
        {
            Tray = new NotificationArea();
            Tray.IconBounds = icon => PrimaryWindow?.TrayIconBounds(icon);
            Tray.TaskbarListCalled += Tracker.Apply;
        }
        // Created up front, so it opens instantly and its app catalog is already loaded.
        _startMenu = new StartMenuWindow(this);
        Notifications = new NotificationCenter();
        _focus = new FocusSession(Notifications);
        _clockFlyout = new ClockFlyout(Notifications, _focus, Settings, RunMode);
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
        Notifications.Start();
        QueueRecreate();
    }

    public void Dispose()
    {
        Settings.Changed -= OnSettingsChanged;
        Tracker.Dispose();
        Tray?.Dispose();
        Indicators?.Dispose();
        _startMenu?.Close();
        _toasts?.Dispose();
        _clockFlyout?.Dispose();
        _focus?.Dispose();
        Notifications?.Dispose();
        CloseWindows();
    }

    /// <summary>Opens Start above <paramref name="taskbar"/>, or closes it if it's open.</summary>
    public void ToggleStartMenu(TaskbarWindow taskbar)
    {
        if (_startMenu is null)
            return;

        if (_startMenu.IsOpen)
        {
            _startMenu.Hide();
        }
        else if (!_startMenu.WasJustDeactivated)
        {
            taskbar.Reveal();
            _startMenu.Show(taskbar.Monitor, taskbar.ScreenBounds, taskbar.Handle, Settings.Current.TaskbarAlignment == TaskbarAlignment.Center, _theme, _accent);
        }
    }

    public bool IsStartMenuOpen => _startMenu?.IsOpen == true;

    /// <summary>Win+1…9: the Nth button on the primary taskbar (0-based here).</summary>
    public void ActivateTask(int index) => PrimaryWindow?.ActivateTask(index);

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
        if (_startMenu is { IsOpen: false } && PrimaryWindow is { } window)
            ToggleStartMenu(window);
    }

    /// <summary>Win+A, Win+Ctrl+V, Win+K, Win+P: Quick Settings on the primary taskbar, open on the page.</summary>
    public void ShowQuickSettings(QuickSettingsPage page) => PrimaryWindow?.ShowQuickSettings(page);

    /// <summary>Win+X: Start's Quick Link menu on the primary taskbar.</summary>
    public void ToggleQuickLinks() => PrimaryWindow?.ToggleQuickLinks();

    /// <summary>Opens the notification center and calendar at the right of <paramref name="taskbar"/>, or closes them.</summary>
    public void ToggleClockFlyout(TaskbarWindow taskbar)
    {
        if (_clockFlyout is null)
            return;

        if (_clockFlyout.IsOpen)
        {
            _clockFlyout.Hide();
        }
        else if (!_clockFlyout.WasJustDeactivated)
        {
            taskbar.Reveal();
            _clockFlyout.Show(taskbar.Monitor, taskbar.ScreenBounds, _theme, _accent);
        }
    }

    /// <summary>Win+N: the notification center on the primary taskbar's monitor.</summary>
    public void ToggleNotificationCenter()
    {
        if (PrimaryWindow is { } window)
            ToggleClockFlyout(window);
    }

    public bool IsClockFlyoutOpen => _clockFlyout?.IsOpen == true;

    public void HideClockFlyout() => _clockFlyout?.Hide();

    /// <summary>Win+T: puts the keyboard focus on the primary taskbar's buttons.</summary>
    public void FocusTaskbar()
    {
        PrimaryWindow?.Reveal();
        PrimaryWindow?.FocusTaskList();
    }

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
        }
    }

    public void ToggleDesktop() => _showDesktop.Toggle();

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
            || current.AutoHide != _windowSettings.AutoHide
            || current.ShowSearchButton != _windowSettings.ShowSearchButton)
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

            if (_recreate)
            {
                _recreate = false;
                CreateWindows();
                return;
            }

            foreach (TaskbarWindow window in _windows)
            {
                window.SetTheme(_theme, _accent);
                window.UpdateClock();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Updating the taskbars failed", ex);
        }
    }

    private void CreateWindows()
    {
        CloseWindows();

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
        UpdateFullScreen();
    }

    private void CloseWindows()
    {
        foreach (TaskbarWindow window in _windows)
            window.Close();
        _windows.Clear();
    }
}
