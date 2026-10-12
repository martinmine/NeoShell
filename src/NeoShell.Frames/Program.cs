using NeoShell.Interop;
using NeoShell.Interop.Windowing;

namespace NeoShell.Frames;

/// <summary>
/// NeoShell.Frames.exe: draws XP's Luna frame over other apps' windows with overlay windows of its own, as NeoShell's
/// settings say (docs/design/window-frames-overlay.md). Started elevated by its logon task; <c>/exit</c> asks the
/// running one to put every window back and exit; <c>/install</c> and <c>/uninstall</c> (elevated) add and remove the
/// task.
/// </summary>
internal static class Program
{
    internal const string InstallArgument = "/install";
    internal const string UninstallArgument = "/uninstall";
    private const string ExitArgument = "/exit";
    private const string SingleInstanceMutexName = @"Local\NeoShell.Frames.SingleInstance";

    private static readonly string s_dataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoShell");

    [STAThread]
    private static int Main(string[] args)
    {
        FrameLog.Initialize(Path.Combine(s_dataDirectory, "logs"));
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case ExitArgument:
                return FrameProgram.Stop() ? 0 : 1;
            case InstallArgument:
                FrameLog.Info("Registering the logon task");
                return LogonTask.Install(Environment.ProcessPath!) ? 0 : 1;
            case UninstallArgument:
                FrameLog.Info("Removing the logon task");
                FrameProgram.Stop();
                return LogonTask.Uninstall() ? 0 : 1;
        }

        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                mutex.Dispose();
                return 0;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return 0; // one runs elevated already
        }

        using (mutex)
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (_, e) => FrameLog.Error("Unhandled exception; the frames go with the process", (Exception)e.ExceptionObject);
            NativeCallback.UnhandledException += ex => FrameLog.Error("Exception in a callback", ex);
            FrameLog.Info($"NeoShell.Frames starting, elevated: {Environment.IsPrivilegedProcess}");

            var loop = new MessageLoop();
            using var host = new FrameHost(loop, Path.Combine(s_dataDirectory, "settings.json"));
            uint exit = WindowMessages.Register(FrameProgram.ExitMessageName);
            uint reload = WindowMessages.Register(FrameProgram.ReloadMessageName);
            using var control = new MessageWindow(FrameProgram.ControlWindowClass, (message, _, _) =>
            {
                if (message == reload)
                    host.Reload();
                else if (message == exit)
                    loop.Quit();
                else
                    return null;
                return 0;
            });
            // NeoShell runs at the user's integrity, below this program's.
            control.AllowFromLowerIntegrity(exit);
            control.AllowFromLowerIntegrity(reload);
            loop.Run();
            FrameLog.Info("NeoShell.Frames exiting");
        }
        return 0;
    }
}
