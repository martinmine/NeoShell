using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>
/// The taskbars: one per monitor (or only on the primary one), recreated when displays, DPI or settings change.
/// </summary>
internal sealed class Taskbars : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Action _exit;
    private readonly ShowDesktop _showDesktop = new();
    private readonly List<TaskbarWindow> _windows = [];
    private ElementTheme _theme = SystemTheme.Read();
    private bool _updateQueued;
    private bool _recreate;

    public Taskbars(RunMode runMode, SettingsStore settings, Action exit)
    {
        RunMode = runMode;
        Settings = settings;
        _exit = exit;
        Settings.Changed += QueueRecreate;
    }

    public RunMode RunMode { get; }

    public SettingsStore Settings { get; }

    public void Show() => QueueRecreate();

    public void Dispose()
    {
        Settings.Changed -= QueueRecreate;
        CloseWindows();
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

        IReadOnlyList<DisplayMonitor> monitors =
            TaskbarLayout.MonitorsWithTaskbar(DisplayMonitor.GetAll(), Settings.Current.ShowOnAllDisplays);
        foreach (DisplayMonitor monitor in monitors)
        {
            var window = new TaskbarWindow(this, monitor, Settings.Current, _theme);
            window.AppWindow.Show(activateWindow: false);
            _windows.Add(window);
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
