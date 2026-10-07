using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>Another account on this PC, as Start's account menu lists it.</summary>
/// <param name="Session">The account's session while it's signed in.</param>
public sealed record OtherUser(string Sid, string Name, uint? Session)
{
    public bool SignedIn => Session is not null;
}

/// <summary>
/// What the menu on Start's user picture offers, read as windows.internal.shell.broker.dll's <c>UserTileCommand</c> and
/// <c>SwitchUserList</c> (behind Explorer's account card) read it. Read it each time the menu opens.
/// </summary>
public sealed record AccountMenu
{
    private const string SystemPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string UserSwitchKey = @"Software\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\UserSwitch";

    /// <summary>The Microsoft account the user signs in with (its email), or null for a local account.</summary>
    public string? MicrosoftAccount { get; init; }
    public bool SignOut { get; init; }
    /// <summary>
    /// A plain "Switch user" to the sign-in screen. Explorer offers it only on a PC joined to a domain or Microsoft
    /// Entra ID, where it can't list the other accounts.
    /// </summary>
    public bool SwitchUser { get; init; }
    /// <summary>"Manage my account" (Settings → Accounts): not for guests or with Control Panel taken away.</summary>
    public bool ManageAccount { get; init; }
    public IReadOnlyList<OtherUser> OtherUsers { get; init; } = [];

    /// <summary>Explorer shows "…" for the other accounts, or for Switch user when it can sign out too.</summary>
    public bool MoreOptions => OtherUsers.Count > 0 || SwitchUser && SignOut;

    public static AccountMenu Read()
    {
        using RegistryKey? startPolicies = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\current\device\Start");
        bool Mdm(string name) => startPolicies?.GetValue(name) as int? == 1;
        bool canSwitch = PowerOptions.CanSwitchUser();
        bool domain = Shlwapi.IsOS(Shlwapi.OS_DOMAINMEMBER);
        bool guest = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Guest);

        return new AccountMenu
        {
            MicrosoftAccount = CurrentUser.MicrosoftAccount(),
            SignOut = PowerOptions.SignOutAllowed() && !Mdm("HideSignOut"),
            SwitchUser = canSwitch && (domain || Power.IsDeviceJoined()) && !Mdm("HideSwitchAccount"),
            ManageAccount = !guest && PowerOptions.Policy(PowerOptions.ExplorerPolicies, "UseDefaultTile") != 1
                && PowerOptions.Policy(PowerOptions.ExplorerPolicies, "NoControlPanel") != 1 && !Mdm("HideChangeAccountSettings"),
            OtherUsers = canSwitch && PowerOptions.Policy(SystemPolicies, "DontEnumerateConnectedUsers", userToo: false) is null or 0
                ? ReadOtherUsers(domain)
                : [],
        };
    }

    /// <summary>
    /// Switches to <paramref name="user"/>: straight to its session's lock screen while it's signed in, else to the
    /// sign-in screen with it chosen. Throws <see cref="Win32Exception"/> when Windows refuses.
    /// </summary>
    public static void SwitchTo(OtherUser user)
    {
        SetUserSwitch(user.Sid);
        if (user.Session is uint session && Winsta.WinStationConnectAndLockDesktop(Wtsapi32.WTS_CURRENT_SERVER_HANDLE, session))
            return;
        Power.SwitchUser();
    }

    /// <summary>The plain Switch user: the sign-in screen with the PC's accounts.</summary>
    public static void SwitchToSignIn()
    {
        SetUserSwitch(null);
        Power.SwitchUser();
    }

    /// <summary>Signed-in accounts first, then by name, as Explorer's <c>CompareUsers</c> sorts them.</summary>
    internal static List<OtherUser> Order(IEnumerable<OtherUser> users) =>
        [.. users.OrderBy(u => u.SignedIn ? 0 : 1).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)];

    // The PC's local accounts that the sign-in screen lists, but not this one. On a domain, the local ones show only
    // while they're signed in.
    private static unsafe List<OtherUser> ReadOtherUsers(bool domain)
    {
        string? self = WindowsIdentity.GetCurrent().User?.Value;
        Dictionary<string, uint> sessions = SignedInSessions();
        using RegistryKey? hidden = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList");
        var users = new List<OtherUser>();
        uint resume = 0;
        int result;
        do
        {
            // NetUserEnum has no level with the SID; NetUserGetInfo's level 23 has it.
            result = Netapi32.NetUserEnum(null, 20, Netapi32.FILTER_NORMAL_ACCOUNT, out nint buffer, Netapi32.MAX_PREFERRED_LENGTH,
                out uint read, out _, ref resume);
            if (result is not (Netapi32.NERR_Success or Netapi32.ERROR_MORE_DATA))
                break;
            try
            {
                foreach (Netapi32.USER_INFO_20 entry in new ReadOnlySpan<Netapi32.USER_INFO_20>((void*)buffer, (int)read))
                {
                    string name = new(entry.Name);
                    if ((entry.Flags & Netapi32.UF_ACCOUNTDISABLE) != 0 || hidden?.GetValue(name) as int? == 0 || Sid(name) is not { } sid || sid == self)
                        continue;
                    uint? session = sessions.TryGetValue(name, out uint id) ? id : null;
                    if (domain && session is null)
                        continue;
                    string fullName = entry.FullName == null ? "" : new string(entry.FullName);
                    users.Add(new OtherUser(sid, fullName.Length > 0 ? fullName : name, session));
                }
            }
            finally
            {
                Netapi32.NetApiBufferFree(buffer);
            }
        }
        while (result == Netapi32.ERROR_MORE_DATA);
        return Order(users);
    }

    private static unsafe string? Sid(string name)
    {
        if (Netapi32.NetUserGetInfo(null, name, 23, out nint buffer) != Netapi32.NERR_Success)
            return null;
        try
        {
            return new SecurityIdentifier(((Netapi32.USER_INFO_23*)buffer)->Sid).Value;
        }
        finally
        {
            Netapi32.NetApiBufferFree(buffer);
        }
    }

    // The sessions of this PC's local accounts that are signed in, by account name.
    private static unsafe Dictionary<string, uint> SignedInSessions()
    {
        var sessions = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (!Wtsapi32.WTSEnumerateSessions(Wtsapi32.WTS_CURRENT_SERVER_HANDLE, 0, 1, out Wtsapi32.WTS_SESSION_INFO* list, out uint count))
            return sessions;
        try
        {
            foreach (Wtsapi32.WTS_SESSION_INFO info in new ReadOnlySpan<Wtsapi32.WTS_SESSION_INFO>(list, (int)count))
            {
                string? user = SessionString(info.SessionId, Wtsapi32.WTSUserName);
                if (!string.IsNullOrEmpty(user) && string.Equals(SessionString(info.SessionId, Wtsapi32.WTSDomainName), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                    sessions[user] = info.SessionId;
            }
        }
        finally
        {
            Wtsapi32.WTSFreeMemory(list);
        }
        return sessions;
    }

    private static unsafe string? SessionString(uint session, int infoClass)
    {
        if (!Wtsapi32.WTSQuerySessionInformation(Wtsapi32.WTS_CURRENT_SERVER_HANDLE, session, infoClass, out char* buffer, out _))
            return null;
        try
        {
            return new string(buffer);
        }
        finally
        {
            Wtsapi32.WTSFreeMemory(buffer);
        }
    }

    // LogonUI reads which account to show first from here; any signed-in user may set values in the key.
    private static void SetUserSwitch(string? sid)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(UserSwitchKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.SetValue);
            if (key is null)
                return;
            if (sid is not null)
                key.SetValue("UserSID", sid, RegistryValueKind.String);
            key.SetValue("Enabled", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The sign-in screen then shows the last account chosen; the switch goes ahead.
        }
    }
}
