# System tray

The system tray (`Shell_TrayWnd`, notify icons, hidden icons) and other apps' app bars. Part of the [NeoShell
design](../design.md).

## System tray (`Tray/`)

- `TrayHost` (Interop) creates a hidden top-level window of class `Shell_TrayWnd` and a child `TrayNotifyWnd`,
  because `Shell_NotifyIcon` finds the tray by that class name. It is placed over the primary taskbar: apps read its
  rectangle to learn where the taskbar is.
- Handles `WM_COPYDATA`:
  - `dwData == 1`: `SHELLTRAYDATA` → `NIM_ADD`, `NIM_MODIFY`, `NIM_DELETE`, `NIM_SETFOCUS`, `NIM_SETVERSION`;
    replies 1 or 0, which the app gets back from `Shell_NotifyIcon`.
  - `dwData == 3`: `Shell_NotifyIconGetRect`, asked twice: `dwMessage` 1 → the top-left corner, 2 → the size, each
    as MAKELONG. An icon in the overflow answers with the chevron, as Explorer does.
  - `dwData == 0`: `SHAppBarMessage` from other apps' app bars (see App bars below).
  - `NOTIFYICONDATA` parsing is done from a byte buffer and handles both 32- and 64-bit callers
    (HWND/HICON fields are 32-bit handles in both, sign-extended) and the V1/V2/V3/current sizes. Unit tested.
- Icons are keyed by (`hWnd`, `uID`) or `guidItem` (`TrayIconStore`, unit tested): adding an existing icon or
  changing a missing one fails, as in Explorer; only flagged fields change. `NIS_HIDDEN` is honoured. Icon pixels
  are copied when they arrive, as the app may destroy its HICON. Windows' own volume, network, power, microphone and
  Meet Now icons (see [Shell service objects](lifecycle.md#shell-service-objects-shell-mode-only-shellsession-interop-shellshellserviceobjectscs)) are kept but not shown, as in Explorer. Tooltips from `szTip` (version
  4 icons without `NIF_SHOWTIP` get `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` instead). Balloon notifications become toasts
  (below).
- **Balloon notifications** (`NIF_INFO`; `TrayBalloon`, `NotificationArea`, Interop `BalloonIcons`,
  `NotifyIconSettings`). How Explorer does it (Windows 11 25H2, `Taskbar.dll`: `NotificationAreaIconManager2::ModifyIcon`
  → `NotificationAreaIcon2::ShowBalloon` → `BalloonToast2::SendAsync`, read with symbols and Ghidra):
  - An empty `szInfo` takes the icon's balloon away (`HideBalloon`), as does deleting the icon; the app hears nothing.
    Otherwise `NIN_BALLOONSHOW` goes to the app at once, before the toast is even posted, and a new balloon from the
    same icon replaces its last without a message for the old one. `uTimeout`, `NIF_REALTIME`,
    `NIIF_RESPECT_QUIET_TIME` and `NIIF_LARGE_ICON` are ignored. (`NIIF_USER` with an `hBalloonIcon` of the wrong
    size is refused by shell32 itself: `Shell_NotifyIcon` returns FALSE and the tray never sees it.)
  - It posts a real toast through `ToastNotificationManager`: `<toast bannerOnly="true">` (a banner only: it never
    stays in the notification center) in the legacy template `ToastImageAndText02` (`…01` without a title,
    `ToastText02`/`…01` without a picture); the title (if 1–127 characters) is the first text, the text the second
    (or the first, without a title); `NIIF_NOSOUND` adds `<audio silent="true"/>`. The picture, written to
    `%TEMP%\{guid}.png`: `NIIF_INFO`/`WARNING`/`ERROR` → the stock icons `SIID_INFO`/`SIID_WARNING`/`SIID_ERROR`
    (79/78/80), `NIIF_USER` → `hBalloonIcon`, else the tray icon, each reloaded large from its file
    (`GetIconInfoEx` module and resource, `SHDefExtractIcon` at 256, 196, 128, 64, 48, 32, 24 or 16, the first
    that loads); `NIIF_NONE` has none.
  - The app: the AppUserModelID of the icon's window when `IApplicationResolver::GetAppIDForWindow` says it is
    explicit (and not a system app); otherwise `NotifyIconGeneratedAumid_<ID>`, `<ID>` being the icon's key in
    `HKCU\Control Panel\NotifyIconSettings` (a random 64-bit number Explorer gives each icon it sees, with its
    `ExecutablePath` and `UID` or `IconGuid`). For that one it registers `HKCU\Software\Classes\AppUserModelId\<AUMID>`
    (volatile) with `DisplayName` = the executable's file description (else its file name) and `IconUri` = the tray
    icon as `%TEMP%\<AUMID>.png`. The notification settings of that AUMID apply (Settings lists it under that name).
    Seen on this VM: the toast's header still shows the raw `NotifyIconGeneratedAumid_7884…` and no logo.
  - The toast's events become messages: `Activated` → `NIN_BALLOONUSERCLICK`; `Dismissed` (timed out, or the
    close button) → `NIN_BALLOONTIMEOUT`, often twice (a time-out raises both `TimedOut` and `UserCanceled`);
    `NIN_BALLOONHIDE` only when the platform hides it. With Do not disturb on the platform drops the banner:
    `NIN_BALLOONSHOW`, then `NIN_BALLOONTIMEOUT` ~65 ms later. Version 4 icons get `wParam` 0 (no anchor) and the
    message and ID in `lParam`; older ones `wParam` = ID, `lParam` = message.
  - NeoShell (shell mode) shows them itself through `ToastPopups`, as any toast (same window, place, stacking,
    timing, hover and slide), with the picture 40 epx large in a 48 epx place beside the text, which centres on it
    (measured on Explorer's: text 80 epx from the left, toast 108 high), and sends the same messages (one
    `NIN_BALLOONTIMEOUT`). It doesn't post them to the notification platform: nothing but NeoShell would show them,
    and `UserNotificationListener` can't describe Explorer's generated apps (`AppInfo` throws "not implemented"; the
    reader skips such notifications). It finds the same app as Explorer (`TrayBalloon.AppIdFor`, unit tested) from
    the window's own AppUserModelID or packaged app (not one set process-wide with
    `SetCurrentProcessExplicitAppUserModelID`, which only the private resolver sees), else Explorer's
    `NotifyIconSettings` key (see Hidden icons below; an icon without one gets the executable's implicit AppID), so the
    user's settings for it apply: no toast with Do not disturb on, banners off for all apps, or the app's
    notifications or banners off; it then times out at once as Explorer's. Names and logo as Explorer registers them
    (file description, tray icon) rather than the raw AUMID Explorer's header shows. A balloon plays the default
    notification sound (see Toast sound in [Notifications and calendar](notifications.md#notifications-and-calendar-notifications)), none with `NIIF_NOSOUND`.
- After `Shell_TrayWnd` exists, broadcast `RegisterWindowMessage("TaskbarCreated")` so running apps re-add icons.
- Remove icons whose owner window has died (`IsWindow` every 5 s and before forwarding input).
- **Mouse forwarding** with `NOTIFYICON_VERSION_4` semantics: `wParam` = anchor point (x, y), `lParam` low word =
  message (`WM_LBUTTONUP`, `NIN_SELECT`, `WM_CONTEXTMENU`, `NIN_POPUPOPEN`…), high word = icon ID; older versions
  get `wParam = uID`, `lParam = mouse message` (version 3 also gets `NIN_SELECT`/`WM_CONTEXTMENU`). Clicks call
  `AllowSetForegroundWindow` for the owner process first. A second press within the double-click time becomes
  `WM_LBUTTONDBLCLK`. Unit tested.
- **Hidden icons** (`NotificationArea`, `TrayIconOrder`, Interop `NotifyIconSettings`; primary taskbar only). Each
  icon is on the taskbar ("promoted") or behind the chevron, one by one, as in Explorer (Windows 11 25H2,
  Taskbar.dll's `NotifyIconSettingsDatabase` / `NotificationAreaIconManager2` and SystemTray.dll's `DragDropManager`
  / `ChevronSystemTrayIconDataModel2`, read with symbols and Ghidra, and watched live with test icons):
  - Explorer's record: `HKCU\Control Panel\NotifyIconSettings` (`Version` 3; below that, or missing, Explorer deletes
    the whole key on start), a key per icon named by a random 64-bit number (`GetRandom64BitInteger`, a
    `mt19937_64`). It's created the first time the shell sees an icon (`AddIcon` →
    `TryGetSettingsForExistingIcon`, else `CreateDefaultSettingsForNewIcon`): `UID`, `ExecutablePath` and
    `InitialTooltip` for an icon without a GUID; `IconGuid` (`{…}`, upper case), `ExecutablePath` (and `Publisher`,
    from the signature) for one with. No `IsPromoted`: **a new icon starts behind the chevron**. Its ID goes first in
    the root's `UIOrderList` (REG_BINARY, the IDs as little-endian 64-bit numbers), one order for all icons, those
    on the taskbar and those in the overflow alike; each row shows its own in that order, an ID missing from it
    counting as first (`GetUIOrderForIcon` returns 0). `IconSnapshot` (a PNG of the first icon) is written once by
    `SetIcon`, for Settings' list. A key matches an icon by executable and GUID, else executable and `UID`.
    `ExecutablePath` starts with a known folder's GUID for System, SystemX86, Windows and Program Files (x64, x86)
    — `{6D809377-…}\VideoLAN\VLC\vlc.exe` — and is a plain path elsewhere (OneDrive under AppData, test apps on E:).
    Windows' own system icons (volume, network…; see [Shell service objects](lifecycle.md#shell-service-objects-shell-mode-only-shellsession-interop-shellshellserviceobjectscs)) get no key.
  - `IsPromoted` (DWORD 1/0) shows an icon on the taskbar. Settings > Personalization > Taskbar > "Other system tray
    icons" lists the keys with a switch each and writes it; each icon watches its own key (a wil registry watcher,
    `OnRegistrySettingsChanged` → `Promoted`), so a change made there (or anywhere) shows at once.
  - "Hidden icon menu" (same page): `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\
    TrayNotify\SystemTrayChevronVisibility`, on unless 0, watched live. Off, the chevron goes and the icons behind it
    show nowhere (not on the taskbar). The chevron also goes while the overflow has no icons, and an open overflow
    closes when its last icon leaves. Glyph by taskbar edge: `E974`/`E972`/`E973`/`E971` (left, top, right, bottom);
    on a bottom taskbar ChevronUpMed `E971` at 16 epx, 10 by 6 pixels lit at 100 %.
  - Dragging: a press on an icon (taskbar or overflow) that moves past the drag threshold starts a drag. The icon
    stays in its place at about 30 % opacity; a copy, 16 epx, follows with its bottom-right corner at the pointer;
    above it, centred, 12 px up, a 30 by 30 caption (rounded 4, about `#2F2F2F` in the dark theme, a 16 px glyph):
    a pin (`E718`) over the taskbar's row for an icon from the overflow, an unpin (`E77A`) over the overflow or the
    chevron for one from the taskbar, none within its own row, and ⊘ ("can't drop here") anywhere else, the gaps
    between icons and the empty part of the overflow included. On an icon, a 1 by 36 epx line
    (`ControlStrongStrokeColorDefault`, about 55 % white), vertically centred, marks where it goes: just left of
    that icon over its left half (`x <= middle`), at its right edge over its right half. Hit testing is by screen
    rectangles of the icons (the overflow's only while it's open), then the chevron's.
  - Dropping (`HandleNotifyIconDragDrop` → `NotificationAreaIconManager2::MoveIcon`): on an icon, the dragged one is
    promoted or not as that row is, and moved before or after it in `UIOrderList` (`NotifyIconSettingsDatabase::
    MoveIcon`: taken out, put back next to the target, or first if the target isn't in the list); into an empty row
    the order stays. On the chevron (overflow open or not): demoted and put first in the overflow (before its first
    icon). On itself or anywhere else: nothing. The moved icon grows into its place from nothing in about 4 frames;
    the others jump. The overflow stays open throughout, also when the drag starts on the taskbar (a press there
    doesn't dismiss it), and grows or shrinks (upwards) by rows as icons come and go. The app hears no click.
    Explorer also moves a focused icon with the keyboard (`MoveIconLeftOrRight`); NeoShell's tray icons don't take
    the keyboard.
  - The overflow: 5 icons a row, 40 by 40 cells, 4 from the edge; newest-first in its order; it opens centred over
    the chevron and doesn't follow it while open.
  - NeoShell as the shell does all this itself, with the same keys and values, so Explorer and Settings see the same
    state after it and the other way round, and follows changes to them while running (`RegistryWatcher` on the
    subtree, and on `TrayNotify`). It skips `Publisher` and `IconSnapshot` (Explorer fills them in when it next sees
    the icon). Its own process's icons (the shell service objects: Safely Remove Hardware…) are filed under
    `%windir%\explorer.exe`, as Explorer files them. Settings can't open in shell mode, so the taskbar menu has
    "Show hidden icon menu"; promoting is by dragging, as in Explorer. The drag picture and the marker are windowed
    popups of the taskbar; the overflow's icons are hit tested from its popup window's real place (TaskbarFlyouts
    moves that window behind WinUI's back). A release over another window reaches the icon only as
    `PointerCaptureLost`, which is then the drop; a press on a taskbar icon cancels the overflow's light dismiss.
  - NeoShell's former `TrayMode` setting (`ShowAll`: every icon on the taskbar) is read once as the shell and cleared:
    `ShowAll` promotes every icon Explorer knows and those added in that session.
- Alongside Explorer: Explorer owns `Shell_TrayWnd`, so NeoShell shows no tray. Fully supported as the shell.

### App bars (`Tray/AppBars.cs`, `Tray/AppBarLayout.cs`, Interop `Tray/AppBarMessage.cs`)

As the shell NeoShell serves other apps' `SHAppBarMessage` calls (docks, launcher bars, apps asking where the
taskbar is) as Explorer does. Found with cdb on shell32 (System32 and SysWOW64) and Ghidra with symbols on
explorer.exe 26200 (`CTray::_OnAppBarMessage`, `_AppBarQueryPos`, `_AppBarSetPos`, `_AppBarSubtractRect(s)`,
`_AppBarOutsideOf`, `StuckAppChange`, `WorkAreaMayHaveChanged`, `AppBarNotifyAll`, `AppBarSetAutoHideBar`,
`OnRudeWindowStateChange`), then compared live with a test bar (64-bit and SysWOW64 PowerShell WinForms) running
the same steps under Explorer and under NeoShell: every reply, rectangle, work area and maximized window matched.

- **Wire format.** shell32 finds `Shell_TrayWnd` and sends `WM_COPYDATA` with `dwData` 0 and 0x40 bytes, the same
  from 32- and 64-bit callers: `APPBARDATA3264` (`cbSize` = 0x28, `hWnd` as 32 bits, `uCallbackMessage`, `uEdge`,
  `rc`, `lParam` as 64 bits), `dwMessage` at 0x28, the shared memory handle at 0x30 (64 bits) and at 0x38 the id of
  the process that handle belongs to (the shell's: shell32 reads it from `Shell_TrayWnd`). A 32-bit caller
  sign-extends `lParam` and the handle and leaves the padding at 0x2C/0x3C uninitialized. For `ABM_QUERYPOS`,
  `ABM_SETPOS` and `ABM_GETTASKBARPOS` shell32 copies the `APPBARDATA3264` into `SHAllocShared` memory for the
  shell; the shell writes `rc` (and `uEdge` for the taskbar position) there with `SHLockShared`, and shell32
  copies it back to the caller's `APPBARDATA` and frees it. The reply is `SendMessage`'s result. shell32 itself
  rejects `cbSize` over 0x30, and calls `ChangeWindowMessageFilterEx` for the callback message on `ABM_NEW`/`REMOVE`.
- **Replies.** `NEW` 1, or 0 for a window already registered; `REMOVE`, `QUERYPOS`, `SETPOS` (even for an
  unregistered bar: its rectangle comes back unchanged), `GETTASKBARPOS`, `ACTIVATE`, `WINDOWPOSCHANGED` and
  `SETSTATE` 1; `GETSTATE` 1 (`ABS_AUTOHIDE`) when the taskbar hides itself, else 0 (never `ABS_ALWAYSONTOP`);
  messages above 12 return 0. `SETSTATE` is accepted but changes nothing in Explorer 26200 (checked live: auto-hide
  stays off), so NeoShell ignores it too. `GETTASKBARPOS` answers the primary taskbar's shown rectangle and
  `ABE_BOTTOM`, also when it's auto-hidden.
- **Placing (`QUERYPOS`).** The proposed rectangle's monitor (`MONITOR_DEFAULTTOPRIMARY`) counts. The bar stays
  clear of the taskbar there (unless it auto-hides), then of the other bars on that monitor that come first: top
  and bottom bars always take precedence over left and right ones; otherwise only bars on the edge asked for count:
  those already outside the bar when it stays on its edge (`_AppBarOutsideOf`, equal counts as outside), all of
  them when it moves to that edge. Only the facing side is clipped (`left = max(left, other.right)` and so on), so
  a rectangle can come out empty or crossed (left past right), and one hanging off the screen is left as it is.
  NeoShell's widget sidebar takes part as a bar on the right. `SETPOS` queries, stores the result and its edge
  (both only if the rectangle changed), and the monitor's work area is worked out again: the monitor less every
  placed bar on it, and less the taskbar and the sidebar (`AppBarLayout.FreeArea`, unit tested). The sidebar keeps
  to the work area's height, so it starts below a top bar.
- **Notifications** (posted with the bar's callback message): `ABN_POSCHANGED` to the other bars on the monitors a
  bar left and joined when one moves or is removed, and to every bar whenever a work area is set and broadcast
  (`WM_SETTINGCHANGE` with `SPI_SETWORKAREA`, from NeoShell (taskbar, sidebar, bars) or any app). Explorer sends
  that second one twice per broadcast (measured: other bars get 3 per move, the mover 2); NeoShell sends it once
  (other bars 2, the mover 1). `ABN_STATECHANGE` to every bar when auto-hide is switched; `ABN_FULLSCREENAPP` (1/0)
  to the bars on a monitor when a full-screen app comes to or leaves it, once per change per bar (Explorer
  remembers what it told each; nothing on `ABM_NEW`). NeoShell's full-screen test is the taskbar's (the foreground
  window covering its monitor, or marked with `MarkFullscreenWindow`); Explorer's rude-window manager also counted
  a covering window that wasn't in front. `ABN_WINDOWARRANGE` goes out around Cascade/Tile, which Windows 11's
  taskbar menu no longer has, so never.
- **Gone bars.** As in Explorer, a bar is dropped (and its space given back) when its thread ends
  (`WindowThread`: a thread-pool wait on the thread handle), and when a notification finds its window gone.
- **Auto-hide bars** (`GET/SETAUTOHIDEBAR(EX)`): one per edge and monitor; `SET` fails when another live bar has
  the edge, `SET` off clears the edge whoever has it. The plain messages use the taskbar's monitor, the `EX` ones
  `MonitorFromRect(rc, MONITOR_DEFAULTTONEAREST)`. Like Explorer, NeoShell also publishes them as properties of
  `Shell_TrayWnd`: `LastAutoHideBarStuckMonitor` (the taskbar's monitor) and `WindowOnEdge:%08x:%1u` (monitor,
  edge) = the bar, or 1 for none. shell32 answers `GETAUTOHIDEBAR(EX)` from them without asking and only sends the
  message when there's none. Explorer's quirk is kept: a bar setting itself again publishes 1, so shell32 then
  answers 0 for that edge. `ACTIVATE`/`WINDOWPOSCHANGED` from a bar lift the auto-hide bar on its edge to the top
  of its band (`SetWindowPos(HWND_TOP)`, no activation; not compared live).
- Alongside Explorer nothing changes: Explorer owns `Shell_TrayWnd` and serves app bars, NeoShell's own included.
