using System.ComponentModel;
using System.Diagnostics;
using NeoShell.Interop.Windowing;

namespace NeoShell.Frames;

/// <summary>
/// NeoShell.Frames.exe as NeoShell sees it, and how the two talk. NeoShell writes the styles to settings.json, which
/// the program reads, and posts it a message (read the settings again, or exit) to its control window, which lets
/// them through from NeoShell's lower integrity. The program runs elevated from a logon task, so it can draw over apps
/// running as administrator too; installing and removing the task asks for administrator rights.
/// </summary>
public static class FrameProgram
{
    internal const string ControlWindowClass = "NeoShell.Frames";
    internal const string ExitMessageName = "NeoShell.Frames.Exit";
    internal const string ReloadMessageName = "NeoShell.Frames.Reload";
    // On every window that has Luna's caption colours, so they can be taken off after the program ended without
    // putting them back (crashed, or ended from Task Manager): its overlays went with it, the colours didn't.
    private const string LunaMark = "NeoShell.Frames.Luna";
    private const FrameParts LunaParts = FrameParts.CaptionColor | FrameParts.TextColor | FrameParts.BorderColor;

    /// <summary>The program, next to NeoShell.exe.</summary>
    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "NeoShell.Frames.exe");

    public static bool IsRunning => MessageWindow.Find(ControlWindowClass) != 0;

    /// <summary>Asks the running program to put every window back and exit.</summary>
    public static bool Stop() => Post(ExitMessageName);

    /// <summary>Asks the running program to read the settings again.</summary>
    public static void NotifySettingsChanged() => Post(ReloadMessageName);

    /// <summary>Whether the logon task is registered.</summary>
    public static async Task<bool> IsInstalledAsync() => await LogonTask.RunAsync("/Query") == 0;

    /// <summary>Starts the program through its task: elevated, without a prompt.</summary>
    public static async Task<bool> StartAsync() => await LogonTask.RunAsync("/Run") == 0;

    /// <summary>Registers the logon task and starts the program. Asks for administrator rights: false if refused or failed.</summary>
    public static Task<bool> InstallAsync() => RunElevatedAsync(Program.InstallArgument);

    /// <summary>Stops the program and removes the logon task. Asks for administrator rights.</summary>
    public static Task<bool> UninstallAsync() => RunElevatedAsync(Program.UninstallArgument);

    /// <summary>Takes off Luna's caption colours the program left on a window, unless it's running.</summary>
    public static void PutBackLeftover(nint hwnd)
    {
        if (WindowFrame.IsMarked(hwnd, LunaMark) && !IsRunning)
            PutBack(hwnd);
    }

    /// <summary>Marks a window given Luna's caption colours.</summary>
    internal static void MarkLuna(nint hwnd) => WindowFrame.Mark(hwnd, LunaMark, true);

    internal static bool IsMarkedLuna(nint hwnd) => WindowFrame.IsMarked(hwnd, LunaMark);

    /// <summary>The window's own caption colours back, and its mark off.</summary>
    internal static void PutBack(nint hwnd)
    {
        WindowFrame.Reset(hwnd, LunaParts, darkMode: false);
        WindowFrame.Mark(hwnd, LunaMark, false);
    }

    private static bool Post(string messageName)
    {
        nint window = MessageWindow.Find(ControlWindowClass);
        return window != 0 && WindowMessages.Post(window, WindowMessages.Register(messageName));
    }

    private static async Task<bool> RunElevatedAsync(string argument)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(ExePath, argument) { UseShellExecute = true, Verb = "runas" });
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // The prompt was declined (ERROR_CANCELLED), or the program is missing.
            return false;
        }
    }
}
