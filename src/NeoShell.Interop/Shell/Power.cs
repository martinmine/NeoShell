using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>The Start menu's power options. Each throws <see cref="Win32Exception"/> when Windows refuses.</summary>
public static unsafe class Power
{
    public static void Lock() => Check(User32.LockWorkStation());

    public static void SignOut() => Check(User32.ExitWindowsEx(User32.EWX_LOGOFF, Advapi32.SHTDN_REASON_PLANNED_OTHER));

    public static void Sleep() => Check(PowrProf.SetSuspendState(hibernate: false, force: false, disableWakeEvent: false));

    public static void Restart() => Shutdown(Advapi32.SHUTDOWN_RESTART);

    public static void ShutDown() => Shutdown(Advapi32.SHUTDOWN_POWEROFF);

    private static void Shutdown(uint flags)
    {
        EnableShutdownPrivilege();
        uint error = Advapi32.InitiateShutdown(null, null, 0, flags, Advapi32.SHTDN_REASON_PLANNED_OTHER);
        if (error != 0)
            throw new Win32Exception((int)error);
    }

    // Every user holds SeShutdownPrivilege, but a process has to switch it on before shutting down.
    private static void EnableShutdownPrivilege()
    {
        Check(Advapi32.OpenProcessToken(-1 /* current process */, Advapi32.TOKEN_ADJUST_PRIVILEGES | Advapi32.TOKEN_QUERY, out nint token));
        try
        {
            Check(Advapi32.LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid));
            var privileges = new Advapi32.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = Advapi32.SE_PRIVILEGE_ENABLED };
            Check(Advapi32.AdjustTokenPrivileges(token, false, &privileges, 0, 0, 0));
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    private static void Check(bool succeeded)
    {
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
