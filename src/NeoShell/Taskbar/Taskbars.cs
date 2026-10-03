using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Interop.Tray;
using NeoShell.Settings;
using NeoShell.Tray;
using NeoShell.StartMenu;

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
    private ShellSettings _windowSettings;
    private StartMenuWindow? _startMenu;
    private bool _updateQueued;
    private bool _recreate;

    public Taskbars(RunMode runMode, SettingsStore settings, Action exit, Action switchToExplorer)
    {
        RunMode = runMode;
        Settings = settings;
        _exit = exit;
        _switchToExplorer = switchToExplorer;
        _windowSettings = settings.Current;
        // Start shows icons at up to 32 effective pixels; load them sharp for the densest monitor.
        uint dpi = DisplayMonitor.GetAll().Select(m => m.Dpi).DefaultIfEmpty(96u).Max();
        Icons = new AppIcons((int)Math.Round(32 * dpi / 96.0));
        Tracker = new WindowTracker(Icons);
        Tracker.Changed += RefreshTasks;
        Settings.Changed += OnSettingsChanged;
    }

    public RunMode RunMode { get; }

    public SettingsStore Settings { get; }

    public AppIcons Icons { get; }

    public WindowTracker Tracker { get; }

    /// <summary>The system tray; only when NeoShell is the shell and no other tray is running.</summary>
    public NotificationArea? Tray { get; private set; }

    public void Show()
    {
        Tracker.Start();
        if (RunMode == RunMode.Shell && !TrayHost.IsTrayRunning())
        {
            Tray = new NotificationArea();
            Tray.IconBounds = icon => _windows.FirstOrDefault(w => w.Monitor.IsPrimary)?.TrayIconBounds(icon);
        }
        // Created up front, so it opens instantly and its app catalog is already loaded.
        _startMenu = new StartMenuWindow(this);
        QueueRecreate();
    }

    public void Dispose()
    {
        Settings.Changed -= OnSettingsChanged;
        Tracker.Dispose();
        Tray?.Dispose();
        _startMenu?.Close();
        CloseWindows();
    }

    /// <summary>Opens Start above <paramref name="taskbar"/>, or closes it if it's open.</summary>
    public void ToggleStartMenu(TaskbarWindow taskbar)
    {
        if (_startMenu is null)
            return;

        if (_startMenu.IsOpen)
            _startMenu.Hide();
        else if (!_startMenu.WasJustDeactivated)
            _startMenu.Show(taskbar.Monitor, taskbar.ScreenBounds, Settings.Current.TaskbarAlignment == TaskbarAlignment.Center, _theme);
    }

    public void HideStartMenu() => _startMenu?.Hide();

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
            case WindowMessages.SettingChange: // theme, regional formats
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
        // Where the taskbars go and how they're laid out needs new windows; the rest is just their buttons.
        ShellSettings current = Settings.Current;
        if (current.TaskbarAlignment != _windowSettings.TaskbarAlignment || current.ShowOnAllDisplays != _windowSettings.ShowOnAllDisplays)
            QueueRecreate();
        else
            RefreshTasks();
    }

    private void RefreshTasks()
    {
        foreach (TaskbarWindow window in _windows)
        {
            window.RefreshTasks();
            window.RefreshTray();
        }
    }

    private void QueueUpdate()
    {
        if (_updateQueued)
            return;

        _updateQueued = true;
        _dispatcher.TryEnqueue(Update);
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

            if (_recreate)
            {
                _recreate = false;
                CreateWindows();
                return;
            }

            foreach (TaskbarWindow window in _windows)
            {
                window.SetTheme(_theme);
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
            var window = new TaskbarWindow(this, monitor, _windowSettings, _theme);
            window.RefreshTasks();
            window.AppWindow.Show(activateWindow: false);
            _windows.Add(window);
            if (monitor.IsPrimary)
                Tray?.SetTaskbarBounds(window.ScreenBounds);
        }
        Log.Info($"Taskbars on {monitors.Count} monitor(s)");
    }

    private void CloseWindows()
    {
        foreach (TaskbarWindow window in _windows)
            window.Close();
        _windows.Clear();
    }
}
