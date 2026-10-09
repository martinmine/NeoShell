# Start: account and power menus

Start's account menu and power menus. Part of the [NeoShell design](../design.md).

## Account menu (`StartMenuWindow` `AccountFlyout`, Interop `Shell/AccountMenu`)

On 25H2 the user in Start's bottom row opens an account card, not StartDocked's own `UserTileView` menu (Change
account settings, Lock, Sign out, the other users) that it falls back to without the account control. The card is
`AccountControl`, a React Native for Windows view (Hermes bytecode in
`SystemApps\MicrosoftWindows.Client.Photon_cw5n1h2txyewy\Public\wsxpacks\AccountControl\index.windows.bundle`,
decompiled with hermes-dec; strings in its `assets\strings\<locale>.json`). It gets its actions and the other
accounts from `WindowsUdk.System.UserProfile.AccountSwitcherAction/AccountSwitcherUser` (windowsudk.shellcommon.dll),
which wrap `Windows.Internal.Shell.StartUI.UserTileMenu` in windows.internal.shell.broker.dll (read with its PDB in
Ghidra). NeoShell reads the same things itself each time the card opens (`AccountMenu.Read`).

- **Layout** (measured on Explorer's with UI Automation and screenshots; NeoShell's matches to the pixel): a Flyout,
  placement Top, centred over the user button and kept on the monitor (a left-aligned Start's card sits at the
  screen's left edge), 4 above it; 362 × 141 with its border, corner 8, Windows' grey acrylic even on an
  accent-coloured Start.
  - Header, 8 below the top, 40 high: Microsoft's logo (20 square, the four squares inset 1 with 1 between, as its
    SVG) 16 from the left, 5 apart from "Microsoft" (14 px semibold, secondary text). On the right, 8 from the edge
    and 8 apart: "Sign out" (flat button 78 × 40, 14 px) and "More options" (40 × 40, E712 16 px). Without the
    second, Sign out moves to the edge.
  - The account, 12 below: picture 58 (circle) 16 from the left; 12 apart, centred on it: the name (18 px
    semibold), the Microsoft account's email or "Local account" (12 px secondary), and 3 below a hyperlink
    "Manage my account" ("My Microsoft account" for a Microsoft account; 12 px, padding 1); 18 below it.
  - "…" opens a second Flyout, bottom edge aligned left, 4 below the button, 243 × 91: "Other users" (12 px
    secondary, 16 from the left, 13 down), then 8 below the rows, 4 in from each side: 233 × 48 flat buttons with
    the picture 30 at 9, the name 12 to its right (14 px; "Signed in" under it at 12 px secondary), the name as
    tooltip. Signed-in accounts first, then by name (`CompareUsers`; unit tested). A plain "Switch user" row
    (E748) follows them where Windows offers it.
  - Hover: the subtle fill on Sign out, "…", the rows and the link, as on the user button.
  - Opens with the XAML popup transition (a short fade and a 5 px slide), closes at once, as Explorer's.
    Explorer's card shows its content fading in before its acrylic; WinUI's windowed popup shows the acrylic a few
    frames before the content.
  - Esc closes "…" and then the card, leaving Start open; Tab moves between Sign out, "…" and the link.
- **What shows** (`UserTileCommand::Is…Enabled`):
  - Sign out: not the `StartMenuLogOff` or `NoLogoff` policy, nor MDM `Start/HideSignOut`.
  - Manage my account: not a guest, nor the `UseDefaultTile` or `NoControlPanel` policy, nor MDM
    `Start/HideChangeAccountSettings`.
  - Plain Switch user: fast user switching as for the power menus, and only on a PC joined to a domain or
    Microsoft Entra ID (`IsOS(OS_DOMAINMEMBER)` or `DsrIsDeviceJoined`), nor MDM `Start/HideSwitchAccount`. On a
    workgroup PC Explorer lists the accounts instead.
  - Other users (`SwitchUserList::PopulateUsers`): Windows' user list for the sign-in screen, without
    `DontEnumerateForLogon` accounts and this one; NeoShell takes the enabled local accounts (`NetUserEnum`)
    that `Winlogon\SpecialAccounts\UserList` doesn't hide, named by full name, else account name. On a domain the
    local ones show only while signed in. Signed in = a session of that account (`WTSEnumerateSessions`).
  - "…" (`isMoreOptionsButtonVisible`): other accounts to list, or Switch user and Sign out both offered.
  - A Microsoft account: `NetUserGetInfo` level 24 (`usri24_internet_identity`, the principal name as email).
- **Actions**:
  - Sign out → `Power.SignOut` (`ExitWindowsEx(EWX_LOGOFF)`; the broker calls shell32's `LogoffWindowsDialog(0)`,
    which ends there). Start closes first.
  - Manage my account → `ms-settings:accounts` (`ManageAccountLink`); as the shell Settings can't start, so it
    opens Control Panel's User Accounts (`control.exe /name Microsoft.UserAccounts`).
  - Another account (`UserTileSwitchUser::SwitchTo`): writes its SID to
    `HKLM\...\Authentication\LogonUI\UserSwitch` `UserSID` and `Enabled` = 1, which LogonUI reads to choose it on
    the sign-in screen (Authenticated Users may set values there; .NET needs `ReadWriteSubTree` to write). Signed
    in: `WinStationConnectAndLockDesktop(server, its session)` (winsta.dll, not in the SDK) goes straight to its
    lock screen. Otherwise, or if that fails: `Power.SwitchUser` (`WTSDisconnectSession`; the broker calls shell32's
    `DisconnectWindowsDialog`, which does the same). The broker signs out instead when
    `IsLogoffRequiredForUserSwitching` (low-resource devices, `RtlQueryResourcePolicy` < 11, under a hypervisor
    without its feature bit); not mirrored.
  - Plain Switch user: `Enabled` = 1, then the same disconnect.
  - Verified with cdb breakpoints in a medium-IL NeoShell that skipped the call: Sign out passed
    `ExitWindowsEx(0, 0x80000000)`; a local test account not signed in got `UserSID`/`Enabled` written and
    `WTSDisconnectSession(0, 0xFFFFFFFF, FALSE)`.
- **Not done**: a Microsoft account's extras (Microsoft 365 / Copilot / Game Pass subscription and OneDrive storage
  cards, "View my benefits", the offline banner), which the account control fetches online; a work account's
  (Entra ID) tenant name in place of Microsoft's logo.

## Power menus (`PowerItems`, Interop `Shell/PowerOptions`, `Shell/Power`)

Start's power button, the Quick Link menu's "Shut down or sign out" and the Shut Down Windows dialog all list
shutdownux.dll's choices (`CShutdownChoices`, read with its PDB in Ghidra). Each reads them again as it opens
(`PowerOptions.Read`); `PowerOptions.Choices(menu)` puts them in order (unit tested).

- **Order**: the account choices for that menu, then Sleep, Hibernate, (Update and) Shut down, (Update and) Restart.
  - Start: Lock. Explorer's power menu on 25H2 is Lock, Sleep, Shut down, Restart, with no separator. Sign out is
    under the user picture.
  - Quick Link (twinui.pcshell's `CLauncherTipContextMenu::_EnumerateAndBuildShutdownMenu`, which ORs Sign out into
    the default mask): Sign out.
  - Shut Down Windows dialog (`_BuildShutdownOptionArray`): Switch user, then Sign out. Explorer shows Switch user
    even with only one account on the PC.
- **What decides each choice**:
  - Sleep: S1–S3 or modern standby (`GetPwrCapabilities`), `ShowSleepOption` (default on), MDM `HideSleep`. This
    VM has only S1 and still shows Sleep.
  - Hibernate: `ShowHibernateOption` (default off; Power Options → "Show in Power menu"), S4, and a *full*
    hibernation file (`HiberFileType` 2; Fast Startup alone keeps a reduced one), MDM `HideHibernate`.
  - Lock: `ShowLockOption` (default on), unless `DisableLockWorkstation` is set in Winlogon or in either Policies\System.
  - Sign out: not the `StartMenuLogOff` (=1) or `NoLogoff` policy.
  - Switch user: a console session that isn't remote (`SM_REMOTESESSION`, `SM_REMOTECONTROL`), fast user switching
    (`IsOS(OS_FASTUSERSWITCHING)`), no `HideFastUserSwitching`.
  - `HidePowerOptions` (HKLM) or `NoClose`: no Sleep, Hibernate, Shut down or Restart anywhere. MDM `HideShutDown` and
    `HideRestart` take away just those. Start shows "There are currently no power options available." when nothing
    is left.
  - The `Show…Option` settings are read from the policy (`HKLM\Software\Policies\Microsoft\Windows\Explorer`) first,
    then `HKLM\…\Explorer\FlyoutMenuSettings`, then the default.
- **Updates**: Windows Update writes `HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\ShutdownFlyoutOptions`.
  Bit 8 is Update and shut down and bit 2 is Update and restart; each takes the place of the plain choice unless
  bit 4 or bit 1 keeps it beside it. Explorer ignores the value if it has any other bit set. `…\UX\RebootDowntime`
  holds `DowntimeEstimateLow` and `DowntimeEstimateHigh` in minutes, capped at 60. With them the names read
  "Update and restart (estimate: 5 - 15 min)", or "up to …", "more than …", "… min - 1 hr" (`PowerItems.Estimate`,
  tested). With updates waiting, the dialog picks Update and shut down. Otherwise it picks the power button action
  (`PowerButtonAction` policy, else `Start_PowerButtonAction`, default Shut down). Checked live by setting
  `ShutdownFlyoutOptions` to 0xA for a while (then back to 0). Explorer's Start, Quick Link menu and dialog
  (shutdownux's dialog object shown in a test process) all offered Update and shut down / Update and restart.
- **Start's look**: glyphs Lock E72E, Sleep E708, Hibernate E823, Shut down E7E8, Restart E777. The update choices
  use F1B1 and ED21 (each with a gap at the top right) under an orange `#FF9900` dot (EC83, the same colour in light
  and dark). A menu item takes one icon, so `PowerItems.Icon` adds the dot into the glyph's own grid. Every item
  except Lock has shutdownux's description as its tooltip, as Explorer's.
- **Actions** (`Power`, shutdownux's `_InitiatePowerTransition`, `ShutdownOption::Invoke`, `_GetEnhancedShutdownFlags`):
  - Lock → `LockWorkStation`. Sign out → `ExitWindowsEx(EWX_LOGOFF)`. Sleep / Hibernate → `SetSuspendState(false|true, …)`.
  - Switch user → `WTSDisconnectSession(current)`. The session keeps running and the sign-in screen lists the
    accounts. Explorer's dialog does this through AuthUI's `CLSID_AuthUISessionControl` (shell32 `_Disconnect`),
    and shutdownux's Disconnect calls `WinStationDisconnect` directly.
  - Shut down / Restart → `InitiateShutdown` with a planned reason, after enabling `SeShutdownPrivilege`:
    - Shut down: `SHUTDOWN_POWEROFF | SHUTDOWN_HYBRID` (Fast Startup; Windows ignores it when that's off). With Shift
      held, no hybrid. Update and shut down: `SHUTDOWN_POWEROFF | SHUTDOWN_INSTALL_UPDATES`.
    - Restart: `SHUTDOWN_RESTART`. Update and restart: `| SHUTDOWN_INSTALL_UPDATES`. With Shift held, any restart
      becomes `SHUTDOWN_RESTART_BOOTOPTIONS` (Advanced startup) without updates.
    - "Use my sign-in info to automatically finish setting up after an update" (Settings → Sign-in options; ARSO):
      when `LsaIsUserArsoEnabled` and `LsaIsUserArsoAllowed` (advapi32, not in the SDK headers) both say yes,
      updates add `SHUTDOWN_ARSO`. A plain restart or shut down adds it too unless `DsrIsDeviceJoined` (dsreg)
      says the PC is joined. ARSO is on here, so Update and restart is 0x2044.
    - Before shutting down, `…\WindowsUpdate\Orchestrator\InstallAtShutdown` (default value) is set to whether this
      shut down installs updates, as `ShutdownOption::Invoke` does. Authenticated Users may set values there, so the
      key is opened with `SetValue` rights only.
  - Not mirrored: with `Orchestrator\EnhancedShutdownEnabled` set, shutdownux passes the flags through USO's private
    `UsoCommitHelper` COM interface. That value doesn't exist on this VM. Also not mirrored: Start's confirmation
    when other users are signed in, and its `RestartApps` and sign-in options link; the Quick Link menu and dialog
    going through shell32's `ExitWindowsEx` path (`CommonRestart`) instead of `InitiateShutdown`. The effect is the
    same.
  - Verified with cdb breakpoints in a medium-IL NeoShell that skipped the call: Update and restart passed
    `InitiateShutdownW(null, null, 0, 0x2044, 0x80000000)`, and Switch user passed
    `WTSDisconnectSession(0, 0xFFFFFFFF, FALSE)`.
