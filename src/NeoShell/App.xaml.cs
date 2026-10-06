using System.Diagnostics;
using System.Security;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using NeoShell.Desktop;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Taskbar;
using NeoShell.Themes;
using NeoShell.Widgets;

namespace NeoShell;

public partial class App : Application
{
    /// <summary>Class of the message window that receives <see cref="ExitMessageName"/> from <c>NeoShell.exe /exit</c>.</summary>
    internal const string ControlWindowClass = "NeoShell.Control";
    internal const string ExitMessageName = "NeoShell_Exit";

    private readonly RunMode _runMode;
    private readonly SettingsStore _settings = new(Path.Combine(Program.DataDirectory, "settings.json"));
    private MessageWindow? _controlWindow;
    private readonly ShellRegistration? _shellRegistration;
    private Wallpaper? _wallpaper;
    private Taskbars? _taskbars;
    private Sidebar? _sidebar;
    private ShellSession? _shellSession;
    private bool _shuttingDown;
    private bool _startExplorerOnExit;

    /// <param name="shellRegistration">Held while NeoShell is the shell; released last on exit.</param>
    public App(RunMode runMode, ShellRegistration? shellRegistration)
    {
        _runMode = runMode;
        _shellRegistration = shellRegistration;
        InitializeComponent();

        // A shell keeps running without windows; it only exits through /exit or its menu.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Program.OnUnhandledException(e.Exception, "XAML");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings.Load();
        ShellTheme.Apply(_settings.Current.Theme, this);
        // A theme is applied to each view as it loads: a new one needs a fresh start.
        _settings.Changed += () =>
        {
            if (_settings.Current.Theme != ShellTheme.Current.Kind)
                Restart();
        };

        uint exitMessage = WindowMessages.Register(ExitMessageName);
#if DEBUG
        // Lets tests check the crash fallback: an exception on the UI thread, like a real bug.
        uint testCrashMessage = WindowMessages.Register("NeoShell_TestCrash");
#endif
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _controlWindow = new MessageWindow(ControlWindowClass, (message, _, _) =>
        {
#if DEBUG
            if (message == testCrashMessage)
            {
                dispatcher.Post(() => throw new InvalidOperationException("Test crash requested"));
                return 0;
            }
#endif
            if (message != exitMessage)
                return null;

            Log.Info("Exit requested");
            // Shut down after this message returns rather than destroying the window from inside its own callback.
            dispatcher.Post(Shutdown);
            return 0;
        });

        if (_runMode == RunMode.Shell)
        {
            _wallpaper = new Wallpaper(_settings, () => _taskbars?.ShowShutDownDialog());
            _wallpaper.Show();
        }

        _taskbars = new Taskbars(_runMode, _settings, Shutdown, SwitchToExplorer);
        _taskbars.Show();
        _sidebar = new Sidebar(_runMode, _settings, _taskbars);
        _sidebar.Show();

        if (_shellRegistration is not null)
        {
            _shellSession = new ShellSession(_shellRegistration, _taskbars, Shutdown);
            _shellSession.Start();
        }

        Log.Info("Started");
    }

    /// <summary>
    /// Makes Explorer the shell again: for this user's next sign-in (removes the per-user Shell value) and right now
    /// (starts Explorer once NeoShell has let go of the screen space).
    /// </summary>
    private void SwitchToExplorer()
    {
        Log.Info("Switching to Explorer");
        try
        {
            using RegistryKey? winlogon = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true);
            winlogon?.DeleteValue("Shell", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Log.Warn("Could not remove the per-user Shell value", ex);
        }
        _startExplorerOnExit = true;
        Shutdown();
    }

    private void Restart()
    {
        Log.Info($"Restarting for the {_settings.Current.Theme} theme");
        Program.RestartRequested = true;
        // After the menu that changed the setting has closed.
        DispatcherQueue.GetForCurrentThread().Post(Shutdown);
    }

    private void Shutdown()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;

        Log.Info("Shutting down");
        ShellWorkArea.BeginExit();
        _shellSession?.Dispose();
        // The sidebar and taskbars next: they give the reserved screen space back.
        _sidebar?.Dispose();
        _taskbars?.Dispose();
        _wallpaper?.Dispose();
        ShellWorkArea.Restore();
        // Last, so Explorer started below becomes the shell rather than opening a folder window.
        _shellRegistration?.Dispose();
        _controlWindow?.Dispose();

        if (_startExplorerOnExit)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Could not start Explorer", ex);
            }
        }
        Exit();
    }
}
