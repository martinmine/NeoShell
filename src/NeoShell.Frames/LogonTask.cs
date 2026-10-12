using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace NeoShell.Frames;

/// <summary>
/// The scheduled task that starts NeoShell.Frames.exe elevated at sign-in ("run with highest privileges"), so the user
/// sees one UAC prompt when turning the program on rather than one at every start. Registered through schtasks.exe.
/// </summary>
internal static class LogonTask
{
    public const string Name = @"NeoShell\Window frames";

    /// <summary>Registers the task for the current user, replacing an older one, and runs it. Needs elevation.</summary>
    public static bool Install(string exePath)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"NeoShell.Frames-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, Xml(exePath, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            return Run($"/Create /XML \"{xmlPath}\" /F") == 0 && Run("/Run") == 0;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    /// <summary>Removes the task. Needs elevation.</summary>
    public static bool Uninstall() => Run("/Delete /F") == 0;

    /// <summary>
    /// The task: at the user's sign-in, elevated, with no time limit, at normal priority (a task's default is below
    /// normal, which would let busy apps' frames lag), not stopped on battery.
    /// </summary>
    internal static string Xml(string exePath, string userName)
    {
        string user = SecurityElement.Escape(userName), command = SecurityElement.Escape(exePath);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Draws Windows XP's Luna frames over app windows for NeoShell (Settings, Window frames).</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{command}"</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>Runs schtasks.exe with <paramref name="command"/> on this task; its exit code (0: done).</summary>
    public static int Run(string command)
    {
        using Process process = Start(command);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            FrameLog.Error($"schtasks {command} failed ({process.ExitCode}): {output.Trim()}");
        return process.ExitCode;
    }

    public static async Task<int> RunAsync(string command)
    {
        using Process process = Start(command);
        await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static Process Start(string command) =>
        Process.Start(new ProcessStartInfo("schtasks.exe", $"{command} /TN \"{Name}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
}
