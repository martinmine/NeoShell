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
    private const string WatchArgument = "/watch";
    private const string AfterArgument = "/after";

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoShell");

    /// <summary>Start NeoShell again once this instance has exited (a new theme applies only at start-up).</summary>
    internal static bool RestartRequested { get; set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // The watchdog is this same executable; it never starts the UI.
        if (args is [WatchArgument, var processId] && int.TryParse(processId, out int watchedProcess))
            return Watch(watchedProcess);

        // A restart: the instance that started this one must be gone before this one can take over.
        if (args is [AfterArgument, var previousId] && int.TryParse(previousId, out int previousProcess))
            WaitForExit(previousProcess);

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
        RunMode runMode = shellRegistration is null ? RunMode.AlongsideExplorer : RunMode.Shell;

        Log.Initialize(Path.Combine(DataDirectory, "logs"));
        Log.Info($"NeoShell {typeof(Program).Assembly.GetName().Version} starting, run mode {runMode}");
        if (runMode == RunMode.Shell)
            StartWatchdog();

        // These only log: a crash ends the process, and the watchdog brings Explorer back.
        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => OnUnhandledException((Exception)e.ExceptionObject, "AppDomain");
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
                new UiThread.LoggingSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(runMode, shellRegistration);
        });

        Log.Info("NeoShell exited");
        if (RestartRequested)
            StartAgain();
        Log.Close();
        return 0;
    }

    private static void StartAgain()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{AfterArgument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Could not start NeoShell again", ex);
        }
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using Process previous = Process.GetProcessById(processId);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    internal static void OnUnhandledException(Exception exception, string source) =>
        Log.Error($"Unhandled exception ({source})", exception);

    /// <summary>
    /// Starts the watchdog that brings Explorer back if NeoShell, as the shell, ends any way but cleanly. A crash
    /// handler inside NeoShell can't cover that: WinUI fail-fasts on exceptions in UI callbacks without running
    /// one, and stack overflows or being killed run nothing at all.
    /// </summary>
    private static void StartWatchdog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{WatchArgument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the watchdog; a crash would leave no shell", ex);
        }
    }

    /// <summary>
    /// Waits for NeoShell to exit. A clean exit returns 0; after anything else Explorer is started, unless another
    /// shell has taken over or Windows is shutting down.
    /// </summary>
    private static int Watch(int processId)
    {
        string ending;
        try
        {
            using Process neoShell = Process.GetProcessById(processId);
            // The exit code of a process this one didn't start is only kept if its handle is open before it exits.
            _ = neoShell.SafeHandle;
            neoShell.WaitForExit();
            if (neoShell.ExitCode == 0)
                return 0;
            ending = $"ended with exit code 0x{neoShell.ExitCode:X8}";
        }
        catch (ArgumentException)
        {
            return 0; // already gone before the watch began; its own start-up logged why
        }
        catch (Exception ex)
        {
            // Unsure how NeoShell ended: err on the side of a usable desktop.
            ending = $"could not be watched ({ex.Message})";
        }

        if (ShellRegistration.IsShellRunning() || ShellRegistration.IsSessionEnding())
            return 0;

        Log.Initialize(Path.Combine(DataDirectory, "logs"));
        Log.Error($"NeoShell (process {processId}) {ending}; starting Explorer");
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
        Log.Close();
        return 0;
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
