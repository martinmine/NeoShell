using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>The power menus' choices. Each throws <see cref="Win32Exception"/> when Windows refuses.</summary>
public static unsafe class Power
{
    public static void Lock() => Check(User32.LockWorkStation());

    public static void SignOut() => Check(User32.ExitWindowsEx(User32.EWX_LOGOFF, Advapi32.SHTDN_REASON_PLANNED_OTHER));

    public static void Sleep() => Check(PowrProf.SetSuspendState(hibernate: false, force: false, disableWakeEvent: false));

    public static void Hibernate() => Check(PowrProf.SetSuspendState(hibernate: true, force: false, disableWakeEvent: false));

    /// <summary>
    /// Disconnects this session, which keeps its apps running and shows the sign-in screen with the PC's accounts:
    /// Explorer's Switch user does the same (through AuthUI's session control).
    /// </summary>
    public static void SwitchUser() =>
        Check(Wtsapi32.WTSDisconnectSession(Wtsapi32.WTS_CURRENT_SERVER_HANDLE, Wtsapi32.WTS_CURRENT_SESSION, wait: false));

    /// <param name="installUpdates">Update and shut down: installs the updates Windows Update has waiting.</param>
    /// <param name="shift">Shift was held: a full shut down, without Fast Startup.</param>
    public static void ShutDown(bool installUpdates, bool shift) => Shutdown(restart: false, installUpdates, shift);

    /// <param name="installUpdates">Update and restart: installs the updates Windows Update has waiting.</param>
    /// <param name="shift">Shift was held: restarts into the boot options (Advanced startup), without updating.</param>
    public static void Restart(bool installUpdates, bool shift) => Shutdown(restart: true, installUpdates, shift);

    private static void Shutdown(bool restart, bool installUpdates, bool shift)
    {
        uint flags = ShutdownFlags(restart, installUpdates, shift, IsArsoOn(), IsDeviceJoined());
        // Windows Update reads whether this shut down installs its updates; any signed-in user may write it.
        SetInstallAtShutdown((flags & Advapi32.SHUTDOWN_INSTALL_UPDATES) != 0);
        EnableShutdownPrivilege();
        uint error = Advapi32.InitiateShutdown(null, null, 0, flags, Advapi32.SHTDN_REASON_PLANNED_OTHER);
        if (error != 0)
            throw new Win32Exception((int)error);
    }

    /// <summary>
    /// shutdownux.dll's flags: Shut down is a hybrid one (Fast Startup) unless Shift is held or it updates; Shift
    /// turns any restart into one to the boot options. With "Use my sign-in info to automatically finish setting up"
    /// on (ARSO), an update signs back in afterwards, and so does a plain restart or shut down unless the PC is joined
    /// to a domain.
    /// </summary>
    internal static uint ShutdownFlags(bool restart, bool installUpdates, bool shift, bool arso, bool joined)
    {
        uint flags;
        if (restart)
        {
            flags = Advapi32.SHUTDOWN_RESTART;
            if (shift)
            {
                flags |= Advapi32.SHUTDOWN_RESTART_BOOTOPTIONS;
                installUpdates = false;
            }
        }
        else
        {
            flags = Advapi32.SHUTDOWN_POWEROFF;
            if (!installUpdates && !shift)
                flags |= Advapi32.SHUTDOWN_HYBRID;
        }
        if (installUpdates)
            flags |= Advapi32.SHUTDOWN_INSTALL_UPDATES;
        if (arso && (installUpdates || !joined))
            flags |= Advapi32.SHUTDOWN_ARSO;
        return flags;
    }

    private static bool IsArsoOn()
    {
        int enabled = 0, allowed = 0;
        return Advapi32.LsaIsUserArsoEnabled(0, &enabled) >= 0 && enabled != 0
            && Advapi32.LsaIsUserArsoAllowed(&allowed) >= 0 && allowed != 0;
    }

    private static bool IsDeviceJoined()
    {
        int joined = 0;
        return Dsreg.DsrIsDeviceJoined(&joined, 0) >= 0 && joined != 0;
    }

    private static void SetInstallAtShutdown(bool install)
    {
        try
        {
            // Users may only set values here, so ask for no more than that.
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Orchestrator\InstallAtShutdown", RegistryRights.SetValue);
            key?.SetValue("", install ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Only a hint for Windows Update; the shut down goes ahead without it.
        }
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
