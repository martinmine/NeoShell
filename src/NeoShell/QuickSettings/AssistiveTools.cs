using System.Diagnostics;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.QuickSettings;

/// <summary>
/// Assistive technologies that are apps of their own (Magnifier, Narrator, Live captions, Voice access): on while
/// they run in this session.
/// </summary>
internal static class AssistiveTools
{
    public static bool IsRunning(string executable)
    {
        Process[] processes = Find(executable);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    public static void Set(string name, string executable, bool on)
    {
        if (on)
        {
            if (!IsRunning(executable))
                Launcher.Launch(new PinnedApp(name, Path: Path.Combine(Environment.SystemDirectory, executable)));
            return;
        }

        foreach (Process process in Find(executable))
        {
            using (process)
            {
                try
                {
                    // Closed as the user would close it, from its window's system menu (Magnifier ignores a plain
                    // WM_CLOSE); one without a window (Narrator running in the background) has to be ended.
                    if (process.MainWindowHandle != 0)
                        TopLevelWindows.Close(process.MainWindowHandle);
                    else
                        process.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Warn($"Could not close {name}", ex);
                }
            }
        }
    }

    private static Process[] Find(string executable)
    {
        using Process self = Process.GetCurrentProcess();
        int session = self.SessionId;
        Process[] all = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        Process[] mine = [.. all.Where(process => process.SessionId == session)];
        foreach (Process process in all.Except(mine))
            process.Dispose();
        return mine;
    }
}
