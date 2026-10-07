using Microsoft.Win32;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>A choice in Windows' power menus, in the order the menus list them.</summary>
public enum PowerChoice { SwitchUser, SignOut, Lock, Sleep, Hibernate, UpdateAndShutDown, ShutDown, UpdateAndRestart, Restart }

/// <summary>The menus that offer power choices; each has its own set, as Explorer's.</summary>
public enum PowerMenu
{
    /// <summary>Start's power button: Lock, then the power states. Sign out and Switch user are under the user picture.</summary>
    Start,
    /// <summary>The Quick Link menu's "Shut down or sign out": Sign out, then the power states.</summary>
    QuickLink,
    /// <summary>The Shut Down Windows dialog (Alt+F4 on the desktop): Switch user, Sign out, then the power states.</summary>
    ShutDownDialog,
}

/// <summary>
/// What Windows offers in its power menus, read as shutdownux.dll's <c>CShutdownChoices</c> (behind Start, the Quick
/// Link menu and the Shut Down Windows dialog) reads it. Read it each time a menu opens, as Explorer does.
/// </summary>
public sealed record PowerOptions
{
    internal const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string SystemPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string StartPolicies = @"SOFTWARE\Microsoft\PolicyManager\current\device\Start";
    private const ulong WNF_USO_REBOOT_REQUIRED = 0x41891D38A3BC2075;

    public bool Sleep { get; init; }
    public bool Hibernate { get; init; }
    public bool Lock { get; init; }
    public bool SignOut { get; init; }
    public bool SwitchUser { get; init; }
    /// <summary>The HidePowerOptions or NoClose policy: no sleep, hibernate, shut down or restart anywhere.</summary>
    public bool PowerHidden { get; init; }
    public bool ShutDownHidden { get; init; }
    public bool RestartHidden { get; init; }
    public bool PowerButtonHidden { get; init; }

    /// <summary>
    /// Windows Update's <c>ShutdownFlyoutOptions</c>: 2 offers "Update and restart" and 8 "Update and shut down" in place
    /// of Restart and Shut down; 1 and 4 keep the plain Restart and Shut down beside them.
    /// </summary>
    public int UpdateFlags { get; init; }

    /// <summary>Windows Update's estimate of the update's downtime in minutes, at most 60; 0 when not known.</summary>
    public int EstimateLow { get; init; }
    public int EstimateHigh { get; init; }

    /// <summary>Windows Update waits for a restart (<c>WNF_USO_REBOOT_REQUIRED</c>): Start's power button gets a dot.</summary>
    public bool RebootRequired { get; init; }

    /// <summary>The power button's action in Power Options, the Shut Down Windows dialog's choice unless updates wait.</summary>
    public PowerChoice? PreferredChoice { get; init; }

    public static PowerOptions Read()
    {
        if (!PowrProf.GetPwrCapabilities(out PowrProf.SYSTEM_POWER_CAPABILITIES power))
            power = default;
        using RegistryKey? startPolicies = Registry.LocalMachine.OpenSubKey(StartPolicies);
        bool Mdm(string name) => startPolicies?.GetValue(name) as int? == 1;

        return new PowerOptions
        {
            Sleep = !Mdm("HideSleep") && FlyoutSetting("ShowSleepOption", true)
                && (power.SystemS1 | power.SystemS2 | power.SystemS3 | power.AoAc) != 0,
            // The hibernation file must be the full one: Fast Startup alone keeps a reduced one.
            Hibernate = !Mdm("HideHibernate") && FlyoutSetting("ShowHibernateOption", false) && power.SystemS4 != 0
                && power.HiberFilePresent != 0 && power.HiberFileType == PowrProf.PowerHiberFileTypeFull,
            Lock = !LockDisabled() && FlyoutSetting("ShowLockOption", true),
            SignOut = SignOutAllowed(),
            SwitchUser = CanSwitchUser(),
            PowerHidden = Policy(ExplorerPolicies, "HidePowerOptions", userToo: false) is { } hide && hide != 0
                || Policy(ExplorerPolicies, "NoClose") is { } noClose && noClose != 0,
            ShutDownHidden = Mdm("HideShutDown"),
            RestartHidden = Mdm("HideRestart"),
            PowerButtonHidden = Mdm("HidePowerButton"),
            UpdateFlags = ReadUpdateFlags(),
            EstimateLow = ReadEstimate("DowntimeEstimateLow"),
            EstimateHigh = ReadEstimate("DowntimeEstimateHigh"),
            RebootRequired = ReadRebootRequired(),
            PreferredChoice = ChoiceFromCode(Policy(ExplorerPolicies, "PowerButtonAction") ?? PowerButtonAction()),
        };
    }

    /// <summary>The choices <paramref name="menu"/> shows, in Explorer's order.</summary>
    public IReadOnlyList<PowerChoice> Choices(PowerMenu menu)
    {
        var choices = new List<PowerChoice>();
        if (menu == PowerMenu.ShutDownDialog && SwitchUser)
            choices.Add(PowerChoice.SwitchUser);
        if (menu != PowerMenu.Start && SignOut)
            choices.Add(PowerChoice.SignOut);
        if (menu == PowerMenu.Start && Lock)
            choices.Add(PowerChoice.Lock);
        if (PowerHidden)
            return choices;

        if (Sleep)
            choices.Add(PowerChoice.Sleep);
        if (Hibernate)
            choices.Add(PowerChoice.Hibernate);
        // Unknown bits make Explorer ignore the value, as one from a newer Windows Update.
        int updates = (UpdateFlags & ~0xF) == 0 ? UpdateFlags : 0;
        if (!ShutDownHidden)
            AddWithUpdate(choices, PowerChoice.UpdateAndShutDown, PowerChoice.ShutDown, updates & 8, updates & 4);
        if (!RestartHidden)
            AddWithUpdate(choices, PowerChoice.UpdateAndRestart, PowerChoice.Restart, updates & 2, updates & 1);
        return choices;
    }

    private static void AddWithUpdate(List<PowerChoice> choices, PowerChoice update, PowerChoice plain, int offerUpdate, int keepPlain)
    {
        if (offerUpdate != 0)
            choices.Add(update);
        if (offerUpdate == 0 || keepPlain != 0)
            choices.Add(plain);
    }

    /// <summary>The Shut Down Windows dialog's first choice: updating if it can, else the power button's action, else shutting down.</summary>
    public PowerChoice DefaultChoice(IReadOnlyList<PowerChoice> choices)
    {
        foreach (PowerChoice? choice in (PowerChoice?[])[PowerChoice.UpdateAndShutDown, PreferredChoice, PowerChoice.ShutDown])
        {
            if (choice is { } c && choices.Contains(c))
                return c;
        }
        return choices[0];
    }

    /// <summary>The choice behind one of Explorer's power button action codes (shutdownux's choice bits).</summary>
    internal static PowerChoice? ChoiceFromCode(int? code) => code switch
    {
        0x1 => PowerChoice.SignOut,
        0x2 => PowerChoice.ShutDown,
        0x4 => PowerChoice.Restart,
        0x10 => PowerChoice.Sleep,
        0x40 => PowerChoice.Hibernate,
        0x100 => PowerChoice.SwitchUser,
        0x200 => PowerChoice.Lock,
        _ => null,
    };

    // A policy beats the setting Power Options writes, which beats the default (shutdownux's GetFlyoutMenuSetting).
    private static bool FlyoutSetting(string name, bool byDefault)
    {
        using RegistryKey? policy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
        using RegistryKey? setting = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings");
        return (policy?.GetValue(name) as int? ?? setting?.GetValue(name) as int?) is { } value ? value != 0 : byDefault;
    }

    // DisableLockWorkstation in any of its three places takes Lock away.
    private static bool LockDisabled()
    {
        using RegistryKey? winlogon = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
        using RegistryKey? machine = Registry.LocalMachine.OpenSubKey(SystemPolicies);
        using RegistryKey? user = Registry.CurrentUser.OpenSubKey(SystemPolicies);
        return new[] { winlogon, machine, user }.Any(key => key?.GetValue("DisableLockWorkstation") is int value && value != 0);
    }

    internal static bool SignOutAllowed() =>
        Policy(ExplorerPolicies, "StartMenuLogOff") != 1 && Policy(ExplorerPolicies, "NoLogoff") is null or 0;

    // Switching needs fast user switching, the console session (not a remote one) and no HideFastUserSwitching.
    internal static bool CanSwitchUser() =>
        User32.GetSystemMetrics(User32.SM_REMOTESESSION) == 0
        && User32.GetSystemMetrics(User32.SM_REMOTECONTROL) == 0
        && Shlwapi.IsOS(Shlwapi.OS_FASTUSERSWITCHING)
        && Policy(SystemPolicies, "HideFastUserSwitching", userToo: false) is null or 0
        && Kernel32.WTSGetActiveConsoleSessionId() == (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId;

    internal static int? Policy(string key, string name, bool userToo = true)
    {
        using RegistryKey? machine = Registry.LocalMachine.OpenSubKey(key);
        if (machine?.GetValue(name) is int value)
            return value;
        using RegistryKey? user = userToo ? Registry.CurrentUser.OpenSubKey(key) : null;
        return user?.GetValue(name) as int?;
    }

    private static int? PowerButtonAction()
    {
        using RegistryKey? advanced = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return advanced?.GetValue("Start_PowerButtonAction") as int?;
    }

    private static int ReadUpdateFlags()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator");
        return key?.GetValue("ShutdownFlyoutOptions") as int? ?? 0;
    }

    private static int ReadEstimate(string name)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\RebootDowntime");
        return Math.Clamp(key?.GetValue(name) as int? ?? 0, 0, 60);
    }

    private static unsafe bool ReadRebootRequired()
    {
        uint value = 0;
        uint length = sizeof(uint);
        return Ntdll.NtQueryWnfStateData(WNF_USO_REBOOT_REQUIRED, 0, 0, out _, &value, ref length) >= 0 && value != 0;
    }
}
