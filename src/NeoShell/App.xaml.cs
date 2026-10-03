using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Desktop;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell;

public partial class App : Application
{
    /// <summary>Class of the message window that receives <see cref="ExitMessageName"/> from <c>NeoShell.exe /exit</c>.</summary>
    internal const string ControlWindowClass = "NeoShell.Control";
    internal const string ExitMessageName = "NeoShell_Exit";

    private readonly RunMode _runMode;
    private readonly SettingsStore _settingsStore = new(Path.Combine(Program.DataDirectory, "settings.json"));
    private ShellSettings _settings = new();
    private MessageWindow? _controlWindow;
    private Wallpaper? _wallpaper;
    private Window? _placeholderWindow;
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
        _settings = _settingsStore.Load();

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

        // Placeholder until the taskbar lands in milestone 3.
        _placeholderWindow = new Window { Title = $"NeoShell ({_runMode})" };
        _placeholderWindow.Closed += (_, _) =>
        {
            _placeholderWindow = null;
            Shutdown();
        };
        _placeholderWindow.Activate();

        Log.Info("Started");
    }

    private void Shutdown()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;

        Log.Info("Shutting down");
        _placeholderWindow?.Close();
        _wallpaper?.Dispose();
        _controlWindow?.Dispose();
        Exit();
    }
}
