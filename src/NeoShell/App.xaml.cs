using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Desktop;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Taskbar;

namespace NeoShell;

public partial class App : Application
{
    /// <summary>Class of the message window that receives <see cref="ExitMessageName"/> from <c>NeoShell.exe /exit</c>.</summary>
    internal const string ControlWindowClass = "NeoShell.Control";
    internal const string ExitMessageName = "NeoShell_Exit";

    private readonly RunMode _runMode;
    private readonly SettingsStore _settings = new(Path.Combine(Program.DataDirectory, "settings.json"));
    private MessageWindow? _controlWindow;
    private Wallpaper? _wallpaper;
    private Taskbars? _taskbars;
    private bool _shuttingDown;

    public App(RunMode runMode)
    {
        _runMode = runMode;
        InitializeComponent();

        // A shell keeps running without windows; it only exits through /exit or its menu.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Program.OnUnhandledException(e.Exception, "XAML");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings.Load();

        uint exitMessage = WindowMessages.Register(ExitMessageName);
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _controlWindow = new MessageWindow(ControlWindowClass, (message, _, _) =>
        {
            if (message != exitMessage)
                return null;

            Log.Info("Exit requested");
            // Shut down after this message returns rather than destroying the window from inside its own callback.
            dispatcher.TryEnqueue(Shutdown);
            return 0;
        });

        if (_runMode == RunMode.Shell)
        {
            _wallpaper = new Wallpaper();
            _wallpaper.Show();
        }

        _taskbars = new Taskbars(_runMode, _settings, Shutdown);
        _taskbars.Show();

        Log.Info("Started");
    }

    private void Shutdown()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;

        Log.Info("Shutting down");
        // Taskbars first: they give the reserved screen space back.
        _taskbars?.Dispose();
        _wallpaper?.Dispose();
        _controlWindow?.Dispose();
        Exit();
    }
}
