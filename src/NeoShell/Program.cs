using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoShell.Interop;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;

namespace NeoShell;

public static class Program
{
    private const string SingleInstanceMutexName = @"Local\NeoShell.SingleInstance";

    private static RunMode s_runMode;
    private static int s_explorerStarted;

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoShell");

    [STAThread]
    private static int Main(string[] args)
    {
        bool exitRequested = args.Any(arg => arg.Equals("/exit", StringComparison.OrdinalIgnoreCase));

        // Only the mutex's existence matters, so it is never owned. "Local\" makes it one instance per session.
        using var mutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
            return exitRequested && !RequestExit() ? 1 : 0;
        if (exitRequested)
            return 0;

        // Register as the shell straight away when there is none: a restarting Explorer would take the role within
        // a second or two. Registered here, on the thread WinUI then runs on, the window keeps getting its messages.
        ShellRegistration? shellRegistration = ShellRegistration.IsShellRunning() ? null : TryRegisterShell();
        s_runMode = shellRegistration is null ? RunMode.AlongsideExplorer : RunMode.Shell;

        Log.Initialize(Path.Combine(DataDirectory, "logs"));
        Log.Info($"NeoShell {typeof(Program).Assembly.GetName().Version} starting, run mode {s_runMode}");

        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => OnUnhandledException((Exception)e.ExceptionObject, "AppDomain");
        // Unobserved task exceptions don't end the process, so starting Explorer here would mean two shells.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        NativeCallback.UnhandledException += ex => Log.Error("Exception in a native callback", ex);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(s_runMode, shellRegistration);
        });

        Log.Info("NeoShell exited");
        Log.Close();
        return 0;
    }

    /// <summary>Logs an exception that is about to end the process; as the shell, starts Explorer in its place.</summary>
    internal static void OnUnhandledException(Exception exception, string source)
    {
        Log.Error($"Unhandled exception ({source})", exception);

        // The same exception can arrive through both the XAML and the AppDomain handler.
        if (s_runMode != RunMode.Shell || Interlocked.Exchange(ref s_explorerStarted, 1) != 0)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            Log.Info("Started explorer.exe so the user isn't left without a shell");
        }
        catch (Exception ex)
        {
            Log.Error("Could not start explorer.exe", ex);
        }
    }

    private static ShellRegistration? TryRegisterShell()
    {
        try
        {
            return new ShellRegistration();
        }
        catch (InvalidOperationException)
        {
            // Explorer registered in the meantime.
            return null;
        }
    }

    private static bool RequestExit()
    {
        nint window = MessageWindow.Find(App.ControlWindowClass);
        return window != 0 && WindowMessages.Post(window, WindowMessages.Register(App.ExitMessageName));
    }
}
