using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

public static class ShellRegistration
{
    /// <summary>True when another process (normally Explorer) has registered itself as the shell.</summary>
    public static bool IsShellRunning() => User32.GetShellWindow() != 0;
}
