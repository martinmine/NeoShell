using System.Diagnostics;
using Microsoft.Win32;
using NeoShell.Logging;

namespace NeoShell;

/// <summary>Where a startup app is registered, in the order Explorer runs them.</summary>
public enum StartupSource
{
    MachineRunOnce,
    UserRunOnce,
    MachineRun,
    MachineRun32,
    UserRun,
    CommonStartupFolder,
    UserStartupFolder,
}

/// <param name="Name">The registry value or file name; also what StartupApproved is keyed by.</param>
/// <param name="Command">A command line for registry entries; a file path for startup folder entries.</param>
public sealed record StartupEntry(string Name, string Command, StartupSource Source);

/// <summary>
/// Starts the apps that run at sign-in, as Explorer does (Windows leaves this to the shell): RunOnce, Run and the
/// Startup folders, skipping those disabled in Task Manager, once per session.
/// </summary>
public static class StartupApps
{
    private const string CurrentVersion = @"Software\Microsoft\Windows\CurrentVersion";
    private static readonly string s_sessionInfo = $@"{CurrentVersion}\Explorer\SessionInfo\{Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Whether a shell already ran them in this session: Explorer marks it in volatile keys under SessionInfo, and so
    /// does NeoShell, so restarting NeoShell or switching to Explorer doesn't start everything twice.
    /// </summary>
    public static bool HaveRunThisSession()
    {
        using RegistryKey? marker = Registry.CurrentUser.OpenSubKey($@"{s_sessionInfo}\StartupHasBeenRun");
        return marker is not null;
    }

    public static void RunAll()
    {
        MarkRunThisSession();
        foreach (StartupEntry entry in Read())
        {
            try
            {
                Run(entry);
            }
            catch (Exception ex)
            {
                Log.Warn($"Startup app {entry.Name} ({entry.Source}) failed: {entry.Command}", ex);
            }
        }
    }

    /// <summary>The enabled startup apps in run order.</summary>
    public static IReadOnlyList<StartupEntry> Read()
    {
        var entries = new List<StartupEntry>();
        // HKLM RunOnce entries must be deleted as they run, which takes an administrator; Explorer leaves them to
        // the next administrator sign-in, and so does NeoShell.
        ReadKey(Registry.CurrentUser, $@"{CurrentVersion}\RunOnce", StartupSource.UserRunOnce, entries);
        ReadKey(Registry.LocalMachine, $@"{CurrentVersion}\Run", StartupSource.MachineRun, entries);
        ReadKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupSource.MachineRun32, entries);
        ReadKey(Registry.CurrentUser, $@"{CurrentVersion}\Run", StartupSource.UserRun, entries);
        ReadFolder(Environment.SpecialFolder.CommonStartup, StartupSource.CommonStartupFolder, entries);
        ReadFolder(Environment.SpecialFolder.Startup, StartupSource.UserStartupFolder, entries);
        return [.. entries.Where(entry => IsApproved(ReadApproval(entry)))];
    }

    /// <summary>
    /// Task Manager's on/off switch: a StartupApproved value whose first byte is odd means disabled; no value means
    /// enabled.
    /// </summary>
    public static bool IsApproved(byte[]? approval) => approval is not { Length: > 0 } || (approval[0] & 1) == 0;

    /// <summary>
    /// Splits a Run command line the way CreateProcess does: a quoted program, or else the shortest run of words that
    /// names an existing file (so unquoted "C:\Program Files\App\app.exe /x" works), else the first word.
    /// </summary>
    public static (string Program, string Arguments) SplitCommandLine(string commandLine, Func<string, bool> fileExists)
    {
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            int end = commandLine.IndexOf('"', 1);
            return end < 0
                ? (commandLine[1..], "")
                : (commandLine[1..end], commandLine[(end + 1)..].Trim());
        }

        string[] words = commandLine.Split(' ');
        for (int count = 1; count <= words.Length; count++)
        {
            string candidate = string.Join(' ', words[..count]);
            foreach (string program in (string[])[candidate, candidate + ".exe"])
            {
                if (fileExists(program))
                    return (program, string.Join(' ', words[count..]).Trim());
            }
        }
        return (words[0], string.Join(' ', words[1..]).Trim());
    }

    private static void Run(StartupEntry entry)
    {
        if (entry.Source == StartupSource.UserRunOnce)
        {
            // Deleted first, so a program that hangs or crashes the sign-in doesn't run again next time. A name
            // starting with "!" asks to be deleted only after it ran.
            if (!entry.Name.StartsWith('!'))
                DeleteRunOnce(entry.Name);
            Launch(entry);
            if (entry.Name.StartsWith('!'))
                DeleteRunOnce(entry.Name);
            return;
        }
        Launch(entry);
    }

    private static void Launch(StartupEntry entry)
    {
        ProcessStartInfo startInfo;
        if (entry.Source is StartupSource.CommonStartupFolder or StartupSource.UserStartupFolder)
        {
            startInfo = new ProcessStartInfo(entry.Command);
        }
        else
        {
            (string program, string arguments) = SplitCommandLine(Environment.ExpandEnvironmentVariables(entry.Command), File.Exists);
            startInfo = new ProcessStartInfo(program, arguments) { WorkingDirectory = Path.GetDirectoryName(program) ?? "" };
        }
        startInfo.UseShellExecute = true;
        Process.Start(startInfo)?.Dispose();
        Log.Info($"Started {entry.Name} ({entry.Source})");
    }

    private static void MarkRunThisSession()
    {
        // Volatile: gone at sign-out, like Explorer's own markers.
        using RegistryKey? session = Registry.CurrentUser.CreateSubKey(s_sessionInfo, writable: true, RegistryOptions.Volatile);
        session?.CreateSubKey("StartupHasBeenRun", writable: false, RegistryOptions.Volatile)?.Dispose();
        session?.CreateSubKey("RunStuffHasBeenRun", writable: false, RegistryOptions.Volatile)?.Dispose();
    }

    private static void ReadKey(RegistryKey hive, string path, StartupSource source, List<StartupEntry> entries)
    {
        using RegistryKey? key = hive.OpenSubKey(path);
        if (key is null)
            return;

        foreach (string name in key.GetValueNames())
        {
            if (key.GetValue(name) is string command && !string.IsNullOrWhiteSpace(command))
                entries.Add(new StartupEntry(name, command, source));
        }
    }

    private static void ReadFolder(Environment.SpecialFolder folder, StartupSource source, List<StartupEntry> entries)
    {
        string path = Environment.GetFolderPath(folder);
        if (!Directory.Exists(path))
            return;

        foreach (string file in Directory.GetFiles(path))
        {
            string name = Path.GetFileName(file);
            if (!name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                entries.Add(new StartupEntry(name, file, source));
        }
    }

    private static byte[]? ReadApproval(StartupEntry entry)
    {
        (RegistryKey? hive, string? list) = entry.Source switch
        {
            StartupSource.MachineRun => (Registry.LocalMachine, "Run"),
            StartupSource.MachineRun32 => (Registry.LocalMachine, "Run32"),
            StartupSource.UserRun => (Registry.CurrentUser, "Run"),
            StartupSource.CommonStartupFolder => (Registry.LocalMachine, "StartupFolder"),
            StartupSource.UserStartupFolder => (Registry.CurrentUser, "StartupFolder"),
            _ => ((RegistryKey?)null, (string?)null), // RunOnce can't be switched off
        };
        if (hive is null)
            return null;

        using RegistryKey? key = hive.OpenSubKey($@"{CurrentVersion}\Explorer\StartupApproved\{list}");
        return key?.GetValue(entry.Name) as byte[];
    }

    private static void DeleteRunOnce(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{CurrentVersion}\RunOnce", writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
